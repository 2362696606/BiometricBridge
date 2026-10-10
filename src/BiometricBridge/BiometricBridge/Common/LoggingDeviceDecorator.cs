using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using BiometricBridge.Core;
using BiometricBridge.Core.Extensions;
using BiometricBridge.Core.Models;
using Serilog;

namespace BiometricBridge.Common;

/// <summary>
/// 日志装饰器：为每台设备的连接、断开、采集与释放记录开始、耗时与失败，异常记录后原样抛出。
/// </summary>
/// <remarks>
/// <para>
/// 只加日志、不改语义：调用原样转发给内层设备，异常也只记录不吞 —— 上层看到的成功/失败与未加本装饰器时一致。
/// </para>
/// <para>
/// 刻意排在串行化之外（注册时给了更大的 order，见 <c>App.RegisterTypes</c>）：这样排队等待
/// （<see cref="SerializingDeviceDecorator"/> 的互斥锁）也算进耗时，且"等锁期间被取消"这类异常同样留痕。
/// 但它不是链上最外层的：补帧装饰器还套在外面，免得补出来的帧被本类数进"共收到 N 帧"。
/// </para>
/// <para>
/// 放在 app 程序集而不是 Core：Core 是无任何包依赖的抽象层，日志实现（Serilog）属于宿主关切。
/// </para>
/// </remarks>
public sealed class LoggingDeviceDecorator : IBiometricDeviceDecorator
{
    #region Fileds

    /// <summary>
    /// 设备标识（厂商 + 型号），构造时算一次。
    /// </summary>
    /// <remarks>
    /// Make/Model 来自设备类上的特性，是静态的，不会变；仅 <see cref="IBiometricDevice.SerialNo"/> 是
    /// "连接后才有值"的动态字段，故不纳入此标签。带上它，日志才说得清是哪台设备。
    /// </remarks>
    // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
    private readonly string _deviceLabel;

    /// <summary>
    /// 带设备上下文的日志器。
    /// </summary>
    private readonly ILogger _logger;

    #endregion

    /// <summary>
    /// 构造装饰器。
    /// </summary>
    /// <param name="innerDevice">
    /// 被装饰的设备。
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="innerDevice"/> 为 null。
    /// </exception>
    public LoggingDeviceDecorator(IBiometricDevice innerDevice)
    {
        ArgumentNullException.ThrowIfNull(innerDevice);
        InnerDevice = innerDevice;

        // GetDeviceInfo 会自行剥到最内层，故此处即便拿到的是另一层装饰器也读得到特性。
        var deviceInfo = innerDevice.GetDeviceInfo();
        _deviceLabel = deviceInfo is null
            ? innerDevice.GetType().Name
            : $"{deviceInfo.Make} {deviceInfo.Model}";

        _logger = Log.ForContext("Device", _deviceLabel);
    }

    #region IBiometricDeviceDecorator

    /// <inheritdoc/>
    public IBiometricDevice InnerDevice { get; }

    /// <inheritdoc/>
    /// <remarks>
    /// 属性读取不记日志：太频繁，且不构成一次"操作"。
    /// </remarks>
    public bool IsConnected => InnerDevice.IsConnected;

    /// <inheritdoc/>
    public string? SerialNo => InnerDevice.SerialNo;

    #endregion

    /// <inheritdoc/>
    public Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync("Connect", () => InnerDevice.ConnectAsync(cancellationToken));
    }

    /// <inheritdoc/>
    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        return RunAsync("Disconnect", () => InnerDevice.DisconnectAsync(cancellationToken));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 比通用的操作日志多记两样：<b>请求要采什么</b>（模态／部位／分组／枚数）与<b>结果采到了什么</b>
    /// （逐枚的部位与质量分）。排"没采到"这类问题时，光看操作名与耗时判断不了是"请求要的部位设备
    /// 根本给不出"，还是"镜头前没有人" —— 两者的处置完全不同，而这两种信息只有装饰器这一层同时看得到。
    /// </remarks>
    public async Task<IReadOnlyList<CaptureResult>> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger.Information(
            "采集请求：模态 {Modality}，部位 [{Positions}]，分组 {Group}，枚数 {Count}，超时 {Timeout} ms，期望质量分 {RequestedScore}",
            request.Modality,
            string.Join(", ", request.Positions),
            request.Group,
            request.Count,
            request.Timeout,
            request.RequestedScore);

        var results = await RunAsync(
            "Capture",
            () => InnerDevice.CaptureAsync(request, cancellationToken)).ConfigureAwait(false);

        _logger.Information(
            "采集结果：{ResultCount} 枚，逐枚「部位／质量分」= {Results}",
            results.Count,
            string.Join("; ", results.Select(result => $"{result.Position?.ToString() ?? "null"}／{result.QualityScore}")));

        return results;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 预览是长时操作，故只在开始与结束时各记一条，<b>不逐帧记</b>。
    /// 结束时记下收到的帧数：一帧都没有通常意味着设备侧的流压根没跑起来
    /// （例如手动触发模式其实不出帧，见 <c>EyeIrisDevice.RunPreviewAsync</c> 的备注），
    /// 这是把那种失败变可诊断的地方。
    /// </remarks>
    public async Task RunPreviewAsync(PreviewFrameSink sink, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);

        // 数帧就地一个闭包即可 —— 不必为了让帧流过而生出一个类型。
        // 计数之所以装进 StrongBox：闭包捕获的是只读的引用，而 Interlocked 要的是一个字段的 ref。
        // 设备线程与补帧定时器线程会并发调用，故用原子操作。
        var count = new StrongBox<int>();
        var lastFrameAt = new StrongBox<long>();
        PreviewFrameSink counting = frame =>
        {
            Interlocked.Increment(ref count.Value);
            Volatile.Write(ref lastFrameAt.Value, Stopwatch.GetTimestamp());
            sink(frame);
        };

        try
        {
            await RunAsync("Preview", () => InnerDevice.RunPreviewAsync(counting, cancellationToken))
                .ConfigureAwait(false);
        }
        finally
        {
            // 走到这里流已停，取一次即可。
            var frames = Volatile.Read(ref count.Value);
            if (frames == 0)
            {
                _logger.Warning("预览结束，但一帧都没收到：设备未投出实时图。");
            }
            else
            {
                // 顺带记下最后一帧距结束有多久。停止预览是"取消 → 收尾宽限 → 返回"：
                // 设备若在取消那一刻就停了流，这个间隔约等于那段宽限；若它一路投到最后一刻，
                // 间隔会接近 0 —— 那说明取消并没有同步停流，停止路径得重新评估。
                var idle = Stopwatch.GetElapsedTime(Volatile.Read(ref lastFrameAt.Value));
                _logger.Information(
                    "预览结束，共收到 {FrameCount} 帧；最后一帧在结束前 {IdleMilliseconds} ms。",
                    frames,
                    (long)idle.TotalMilliseconds);
            }
        }
    }

    /// <summary>
    /// 释放内层设备。
    /// </summary>
    /// <returns>
    /// 表示异步释放操作的任务。
    /// </returns>
    public async ValueTask DisposeAsync()
    {
        await RunAsync("Dispose", () => InnerDevice.DisposeAsync().AsTask()).ConfigureAwait(false);
    }

    /// <summary>
    /// 记录一次设备操作的开始、结果与耗时；失败时记下异常再原样抛出。
    /// </summary>
    /// <param name="operation">
    /// 操作名，用于日志区分。
    /// </param>
    /// <param name="action">
    /// 实际操作。
    /// </param>
    /// <returns>
    /// 表示异步操作的任务。
    /// </returns>
    private async Task RunAsync(string operation, Func<Task> action)
    {
        // 借泛型重载复用日志逻辑：无返回值的操作包一层假返回值即可。
        await RunAsync<object?>(operation, async () =>
        {
            await action().ConfigureAwait(false);
            return null;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 记录一次设备操作的开始、结果与耗时；失败时记下异常再原样抛出。
    /// </summary>
    /// <typeparam name="TResult">
    /// 操作返回类型。
    /// </typeparam>
    /// <param name="operation">
    /// 操作名，用于日志区分。
    /// </param>
    /// <param name="action">
    /// 实际操作。
    /// </param>
    /// <returns>
    /// 异步操作的结果。
    /// </returns>
    /// <exception cref="Exception">
    /// 内层操作抛出的异常，记录后原样重抛。
    /// </exception>
    private async Task<TResult> RunAsync<TResult>(string operation, Func<Task<TResult>> action)
    {
        _logger.Information("设备操作开始：{Operation}", operation);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await action().ConfigureAwait(false);
            _logger.Information(
                "设备操作完成：{Operation}，耗时 {ElapsedMilliseconds} ms",
                operation,
                stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (Exception exception)
        {
            // 记录后重抛：装饰器不该改变失败语义，上层的 catch 照旧生效。
            _logger.Error(
                exception,
                "设备操作失败：{Operation}，耗时 {ElapsedMilliseconds} ms",
                operation,
                stopwatch.ElapsedMilliseconds);
            throw;
        }
    }
}
