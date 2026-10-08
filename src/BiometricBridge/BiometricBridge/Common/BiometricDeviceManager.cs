using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using BiometricBridge.Core;
using BiometricBridge.Core.Extensions;
using BiometricBridge.Core.Models;
using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Common;

/// <summary>
/// 设备管理器(应为单例)。设备操作与设备生命周期的唯一入口。
/// </summary>
/// <remarks>
/// <para>
/// 设备不会流出管理器：外部只拿到 <see cref="DeviceSnapshot"/> 只读快照，所有操作都按设备 id 在此转发。
/// 因此"状态变了却没通知"在结构上不可能发生 —— 状态只能由本类改变，而本类每次改变都会发事件。
/// </para>
/// <para>
/// 不引用任何 UI 类型。<see cref="DeviceStateChanged"/> 在设备调用完成的线程上同步触发；本类与设备层
/// 一律 <c>ConfigureAwait(false)</c>，故通常是线程池线程，需要回到 UI 线程由订阅方自行调度。
/// <b>不要给本类的 await 加回 <c>ConfigureAwait(true)</c> 或删掉它</b>：那会让事件落在调用方所在的
/// 线程上（从 UI 线程调用时就是 UI 线程），使上述线程契约变成随调用点而变，订阅方的调度也随之失效。
/// </para>
/// <para>
/// 预览归本类仲裁：预览期间设备被占用，故连接、断开、采集都<b>先停预览</b>再转发
/// （原生的 <c>biospi_cancel</c> 是进程级的，不是按设备的，两台设备同时流传不出去 ——
/// 当前只挂了一台虹膜设备，故不构成问题；日后并挂多台时，需要把原生调用改为按<b>厂商</b>共享的门，
/// 而不是现在这个按设备实例的门）。
/// </para>
/// </remarks>
public sealed class BiometricDeviceManager : IAsyncDisposable
{
    #region Fileds

    /// <summary>
    /// 设备字典。
    /// </summary>
    /// <remarks>
    /// 设备只在构造时加入，运行期不增删，故用并发字典只为让
    /// <see cref="GetDeviceSnapshots"/> 能安全地在 <see cref="DisposeAsync"/> 清空字典的同时枚举。
    /// </remarks>
    private readonly ConcurrentDictionary<Guid, ManagedDevice> _devices = new();

    #endregion

    /// <summary>
    /// 构造管理器并接管给定设备。
    /// </summary>
    /// <param name="devices">
    /// 设备集合。管理器持有它们，并在 <see cref="DisposeAsync"/> 时释放。
    /// </param>
    public BiometricDeviceManager(IReadOnlyList<IBiometricDevice> devices)
    {
        foreach (var biometricDevice in devices)
        {
            _devices.TryAdd(Guid.NewGuid(), new ManagedDevice { Device = biometricDevice });
        }
    }

    #region Events

    /// <summary>
    /// 设备状态发生变化时触发（连接、断开成功后，或手动设置状态后，各一次）。
    /// </summary>
    /// <remarks>
    /// 在设备调用完成的线程上同步触发（见类型说明），事件参数已含变更后的完整快照，订阅方无需回头查询。
    /// 操作失败时不触发：状态并未改变。
    /// </remarks>
    public event EventHandler<DeviceStateChangedEventArgs>? DeviceStateChanged;

    /// <summary>
    /// 预览启停时触发。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 触发时机：请求开始且会话已装好、或预览自行结束（设备侧出错、被连接的其它操作顶掉）。
    /// <b>调用方主动停止不会触发</b> —— 那次调用方本来就知道，再广播反而会与新起的会话打架
    /// （见 <see cref="PreviewSession.StopRequested"/>）。开始的那次一定先于可能的结束触发。
    /// </para>
    /// <para>
    /// 必须有这个通知：设备侧的补帧会一直重投最后一帧，流悄悄死掉时画面与活着时长得一模一样，
    /// 没有它界面就会一直停在冻结的画面上。
    /// </para>
    /// <para>
    /// 线程约定同 <see cref="DeviceStateChanged"/>：在改状态的线程上同步触发，订阅方自行调度。
    /// </para>
    /// </remarks>
    public event EventHandler<PreviewStateChangedEventArgs>? PreviewStateChanged;

    #endregion

    #region 查询

    /// <summary>
    /// 读取全部设备的快照。
    /// </summary>
    /// <returns>
    /// 设备快照列表。调用方拿到的是值快照，不含设备对象，可安全跨线程持有。
    /// </returns>
    /// <remarks>
    /// 每次调用都重新读取设备信息，故 <see cref="DeviceInfo.SerialNo"/> 这类随时间变化的字段总是最新值
    /// —— 设备只在连接后才填报 SN，缓存快照会把 SN 永久冻结为 null。
    /// </remarks>
    public IReadOnlyList<DeviceSnapshot> GetDeviceSnapshots()
    {
        return _devices
            .Select(pair => CreateSnapshot(pair.Key, pair.Value))
            .ToArray();
    }

    #endregion

    #region 操作

    /// <summary>
    /// 连接设备。
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id。
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌。
    /// </param>
    /// <exception cref="KeyNotFoundException">
    /// 设备 id 不存在。
    /// </exception>
    public async Task ConnectAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        var managedDevice = Resolve(deviceId);

        // 预览占着设备，先收掉再动它（见 PreviewStateChanged 的说明）。
        await StopPreviewAsync(deviceId, cancellationToken).ConfigureAwait(false);

        await managedDevice.Device.ConnectAsync(cancellationToken).ConfigureAwait(false);
        OnDeviceStateChanged(deviceId, managedDevice);
    }

    /// <summary>
    /// 断开设备。
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id。
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌。
    /// </param>
    /// <exception cref="KeyNotFoundException">
    /// 设备 id 不存在。
    /// </exception>
    public async Task DisconnectAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        var managedDevice = Resolve(deviceId);

        // 断开前必须停预览：设备层的原生流还活着时归还句柄，就是访问已释放的内存。
        await StopPreviewAsync(deviceId, cancellationToken).ConfigureAwait(false);

        await managedDevice.Device.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        OnDeviceStateChanged(deviceId, managedDevice);
    }

    /// <summary>
    /// 手动设置设备状态。
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id。
    /// </param>
    /// <param name="status">
    /// 目标状态。
    /// </param>
    /// <exception cref="KeyNotFoundException">
    /// 设备 id 不存在。
    /// </exception>
    /// <remarks>
    /// 同步：不触碰设备，只改管理器持有的状态并广播。值未变时不发事件 —— 与"状态真的变了才通知"
    /// 一致，也让调用方可以放心地把"回填当前值"这类动作写回来（自动退化为空操作）。
    /// 线程同 <see cref="ConnectAsync"/> 的说明：事件在调用线程上同步触发。
    /// </remarks>
    public void SetDeviceStatus(Guid deviceId, BiometricDeviceStatus status)
    {
        var managedDevice = Resolve(deviceId);
        if (managedDevice.DeviceStatus == status)
        {
            return;
        }

        managedDevice.DeviceStatus = status;
        OnDeviceStateChanged(deviceId, managedDevice);
    }

    /// <summary>
    /// 采集生物特征。
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id。
    /// </param>
    /// <param name="request">
    /// 采集请求。
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌。
    /// </param>
    /// <returns>
    /// 采集结果集合。一次调用可能返回多条（如四指联采）。
    /// </returns>
    /// <exception cref="KeyNotFoundException">
    /// 设备 id 不存在。
    /// </exception>
    /// <remarks>
    /// 不触发 <see cref="DeviceStateChanged"/>：采集不改变连接状态。
    /// </remarks>
    public async Task<IReadOnlyList<CaptureResult>> CaptureAsync(
        Guid deviceId,
        CaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        var managedDevice = Resolve(deviceId);

        // 采集与预览互斥：原生侧同时只跑得了一个采集。先停预览，否则这次采集会以"状态非法"失败。
        await StopPreviewAsync(deviceId, cancellationToken).ConfigureAwait(false);

        return await managedDevice.Device.CaptureAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 开始持续预览，把设备的实时图投给 <paramref name="sink"/>。
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id。
    /// </param>
    /// <param name="sink">
    /// 帧接收端。典型实现是界面侧的预览视图模型。
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌，只作用于"启动前先停掉旧会话"这一步；预览本身的停止走
    /// <see cref="StopPreviewAsync"/>。
    /// </param>
    /// <returns>
    /// 表示启动操作的任务。返回时设备层的流已受理。
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="sink"/> 为 null。
    /// </exception>
    /// <exception cref="KeyNotFoundException">
    /// 设备 id 不存在。
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 设备未连接。
    /// </exception>
    /// <remarks>
    /// 先停掉该设备上的旧会话再起新的：两次异步采集重叠会让 <c>biospi_cancel</c> 停错流。
    /// 流若随后自行失败结束，由 <see cref="PreviewStateChanged"/> 告知，本方法不再回头报错。
    /// </remarks>
    public async Task StartPreviewAsync(
        Guid deviceId,
        PreviewFrameSink sink,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var managedDevice = Resolve(deviceId);

        await StopPreviewAsync(deviceId, cancellationToken).ConfigureAwait(false);

        if (!managedDevice.Device.IsConnected)
        {
            throw new InvalidOperationException($"设备 {deviceId} 未连接，无法预览。");
        }

        var session = new PreviewSession(sink);
        lock (managedDevice.PreviewGate)
        {
            if (managedDevice.Preview is not null)
            {
                throw new InvalidOperationException($"设备 {deviceId} 已有预览在进行中。");
            }

            managedDevice.Preview = session;

            // 广播"开始"要在起流之前、且在半锁内：起流可能当场失败并立刻广播"结束"，
            // 顺序反了订阅方就会先收到"结束"再收到"开始"，最后卡在错误的状态上。
            // （订阅方须立即返回，别在本回调里同步回调本类 —— 界面侧是 Post，不会。）
            OnPreviewStateChanged(deviceId, isPreviewing: true);

            // 会话一装好就带上自己的结束任务，免得 StopPreviewAsync 撞上"已登记但还没起跑"的空档。
            session.Completion = Task.Run(() => RunPreviewSessionAsync(deviceId, managedDevice, session));
        }
    }

    /// <summary>
    /// 停止设备上的预览。可重复调用：没有预览在跑时直接返回。
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id。
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌，只作用于"先停掉旧会话"这一步。
    /// </param>
    /// <returns>
    /// 表示停止操作的任务。<b>返回时设备已静默</b>：串行化门已释放，可以接连接/断开/采集。
    /// </returns>
    /// <exception cref="KeyNotFoundException">
    /// 设备 id 不存在。
    /// </exception>
    /// <remarks>
    /// 幂等，且是"停预览"的唯一收口——本类的连接、断开、采集都先走它。
    /// <b>它不抢设备访问的门</b>：预览任务正持着那把门且要等取消才放开，抢它必然死锁。
    /// </remarks>
    public async Task StopPreviewAsync(Guid deviceId, CancellationToken cancellationToken = default)
    {
        var managedDevice = Resolve(deviceId);

        PreviewSession? session;
        lock (managedDevice.PreviewGate)
        {
            session = managedDevice.Preview;
            if (session is null)
            {
                return;
            }

            // 先摘掉标记再取消：结束通知据此判断"不是调用方主动停的"，新会话也可立刻起。
            session.StopRequested = true;
            managedDevice.Preview = null;
        }

        // 取消即请求设备停流。放在锁外：取消会同步跑注册的回调（原生 biospi_cancel）。
        session.Cancellation.Cancel();

        try
        {
            // 等流真正结束。
            await session.Completion.ConfigureAwait(false);

            // 设备侧在宽限内没等到结束回调会失败，这里必须让它浮出来：那种情况下原生流可能还活着，
            // 放行后续操作（尤其是断开）就是访问已释放的内存，宁可让调用方失败也不装作停好了。
            if (session.Failure is { } failure)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        finally
        {
            session.Cancellation.Dispose();
        }
    }

    #endregion

    /// <summary>
    /// 跑一次预览会话，并在它结束时收尾。
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id。
    /// </param>
    /// <param name="managedDevice">
    /// 设备条目。
    /// </param>
    /// <param name="session">
    /// 会话。
    /// </param>
    /// <returns>
    /// 表示会话结束的任务。
    /// </returns>
    /// <remarks>
    /// 本方法由 <see cref="StartPreviewAsync"/> 经 <see cref="Task.Run(Func{Task})"/> 起跑，
    /// 故它的同步段不会跑在启动方的锁里或 UI 线程上。
    /// </remarks>
    private async Task RunPreviewSessionAsync(Guid deviceId, ManagedDevice managedDevice, PreviewSession session)
    {
        try
        {
            await managedDevice.Device.RunPreviewAsync(session.Sink, session.Cancellation.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 取消是预期的停止路径，不算失败。
        }
        catch (Exception exception)
        {
            // 收在会话上而不是让任务带故障：自行结束的会话没人 await，故障任务会变成"未观测异常"。
            // 需要知道失败的一方由 StopPreviewAsync 负责重抛。
            session.Failure = exception;
        }
        finally
        {
            var notify = false;
            lock (managedDevice.PreviewGate)
            {
                if (ReferenceEquals(managedDevice.Preview, session))
                {
                    // 流是自己结束的（设备侧出错、被顶掉等）：摘掉引用，下一个会话才起得来。
                    managedDevice.Preview = null;
                    notify = !session.StopRequested;
                }
            }

            if (notify)
            {
                // 主动停的那条路由 StopPreviewAsync 收尾（释放取消源 + 不广播），这里只管自行结束的情形。
                session.Cancellation.Dispose();
                OnPreviewStateChanged(deviceId, isPreviewing: false);
            }
        }
    }

    /// <summary>
    /// 触发预览状态变化事件。
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id。
    /// </param>
    /// <param name="isPreviewing">
    /// 变更后是否正在预览。
    /// </param>
    private void OnPreviewStateChanged(Guid deviceId, bool isPreviewing)
    {
        PreviewStateChanged?.Invoke(this, new PreviewStateChangedEventArgs
        {
            DeviceId = deviceId,
            IsPreviewing = isPreviewing,
        });
    }

    /// <summary>
    /// 释放全部设备。
    /// </summary>
    /// <returns>
    /// 表示异步释放操作的任务。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 幂等：第二次调用时字典已空。调用方应确保此时没有采集在进行 ——
    /// <see cref="SerializingDeviceDecorator.DisposeAsync"/> 不快照互斥锁，采集途中释放会放掉原生代码
    /// 正在使用的句柄。
    /// </para>
    /// <para>
    /// 预览则由本类自行收掉，不等调用方：预览同样握着原生流，而释放装饰器同样不抢那把锁，顺序反了就是
    /// 放掉原生仍在使用的句柄。顺带这也在释放设备之前停掉了设备侧的补帧定时器 —— 否则它会一直投帧，
    /// 而接收端（界面）此时已经拆到一半了。
    /// </para>
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        foreach (var deviceId in _devices.Keys)
        {
            await StopPreviewAsync(deviceId).ConfigureAwait(false);
        }

        foreach (var managedDevice in _devices.Values)
        {
            await managedDevice.Device.DisposeAsync().ConfigureAwait(false);
        }

        _devices.Clear();
    }

    /// <summary>
    /// 按 id 取设备条目。
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id。
    /// </param>
    /// <returns>
    /// 设备条目。
    /// </returns>
    /// <exception cref="KeyNotFoundException">
    /// 设备 id 不存在。
    /// </exception>
    private ManagedDevice Resolve(Guid deviceId)
    {
        if (_devices.TryGetValue(deviceId, out var managedDevice))
        {
            return managedDevice;
        }

        throw new KeyNotFoundException($"未找到设备 {deviceId}。");
    }

    /// <summary>
    /// 构造单个设备的快照。
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id。
    /// </param>
    /// <param name="managedDevice">
    /// 设备条目。
    /// </param>
    /// <returns>
    /// 设备快照。
    /// </returns>
    private static DeviceSnapshot CreateSnapshot(Guid deviceId, ManagedDevice managedDevice)
    {
        return new DeviceSnapshot(
            deviceId,
            managedDevice.Device.GetDeviceInfo(),
            managedDevice.DeviceStatus,
            managedDevice.Device.IsConnected);
    }

    /// <summary>
    /// 触发状态变化事件。
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id。
    /// </param>
    /// <param name="managedDevice">
    /// 设备条目。
    /// </param>
    private void OnDeviceStateChanged(Guid deviceId, ManagedDevice managedDevice)
    {
        DeviceStateChanged?.Invoke(this, new DeviceStateChangedEventArgs
        {
            Snapshot = CreateSnapshot(deviceId, managedDevice),
        });
    }
}
