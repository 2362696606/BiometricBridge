using System;
using System.Collections.Generic;
using System.Diagnostics;
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
/// 刻意处于装饰链最外层（注册时给了更大的 order，见 <c>App.RegisterTypes</c>）：这样排队等待
/// （<see cref="SerializingDeviceDecorator"/> 的互斥锁）也算进耗时，且"等锁期间被取消"这类异常同样留痕。
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
    public Task<IReadOnlyList<CaptureResult>> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        return RunAsync("Capture", () => InnerDevice.CaptureAsync(request, cancellationToken));
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
