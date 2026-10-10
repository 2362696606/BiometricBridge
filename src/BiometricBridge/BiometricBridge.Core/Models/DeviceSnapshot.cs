using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Core.Models;

/// <summary>
/// 设备只读快照：对外代表一台设备的全部信息。
/// </summary>
/// <param name="DeviceId">
/// 设备 id，即设备字典的键。<see cref="DeviceInfo"/> 自身不携带 id，故在此单列。
/// </param>
/// <param name="Info">
/// 设备静态信息；设备类漏标 <c>BiometricDeviceInfoAttribute</c> 时为 null。
/// </param>
/// <param name="DeviceStatus">
/// 设备状态。
/// </param>
/// <param name="IsConnected">
/// 设备是否已连接。
/// </param>
/// <remarks>
/// 不含设备对象，可安全跨线程持有。因 <see cref="DeviceInfo.SerialNo"/> 是"连接后才有值"的动态
/// 字段，本类型不可缓存 —— 每次读取都应重新构造。
/// </remarks>
public sealed record DeviceSnapshot(
    Guid DeviceId,
    DeviceInfo? Info,
    BiometricDeviceStatus DeviceStatus,
    bool IsConnected);
