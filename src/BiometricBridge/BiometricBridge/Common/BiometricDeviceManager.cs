using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
    /// 设备状态发生变化时触发（连接、断开成功后各一次）。
    /// </summary>
    /// <remarks>
    /// 在设备调用完成的线程上同步触发（见类型说明），事件参数已含变更后的完整快照，订阅方无需回头查询。
    /// 操作失败时不触发：状态并未改变。
    /// </remarks>
    public event EventHandler<DeviceStateChangedEventArgs>? DeviceStateChanged;

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
        await managedDevice.Device.DisconnectAsync(cancellationToken).ConfigureAwait(false);
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
        return await managedDevice.Device.CaptureAsync(request, cancellationToken).ConfigureAwait(false);
    }

    #endregion

    /// <summary>
    /// 释放全部设备。
    /// </summary>
    /// <returns>
    /// 表示异步释放操作的任务。
    /// </returns>
    /// <remarks>
    /// 幂等：第二次调用时字典已空。调用方应确保此时没有采集在进行 ——
    /// <see cref="SerializingDeviceDecorator.DisposeAsync"/> 不快照互斥锁，采集途中释放会放掉原生代码
    /// 正在使用的句柄。
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
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
