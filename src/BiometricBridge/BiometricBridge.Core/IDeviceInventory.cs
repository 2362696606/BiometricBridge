using BiometricBridge.Core.Models;

namespace BiometricBridge.Core;

/// <summary>
/// 设备清单：本机有哪些生物识别设备，以及各自的当前快照
/// </summary>
/// <remarks>
/// <para>
/// 对外服务（如 SBI 的设备发现）要枚举本机设备，而设备实例本身不流出管理器。本接口就是那道窄门：
/// 上层只说"给我清单"，不去碰设备。
/// </para>
/// <para>
/// 落在设备抽象层而不是某个具体实现里，是为了让宿主层与设备管理者两边都只认得它 —— 宿主不必知道
/// 设备从哪儿来，换一套设备管理实现时本接口不动。
/// </para>
/// <para>
/// 实现方须保证每次调用都取最新值（<see cref="DeviceInfo.SerialNo"/> 这类字段连接后才有值），
/// 故不允许返回缓存的旧快照。
/// </para>
/// </remarks>
public interface IDeviceInventory
{
    /// <summary>
    /// 读取全部设备的当前快照
    /// </summary>
    /// <returns>
    /// 设备快照列表。调用方拿到的是值快照，不含设备对象，可安全跨线程持有
    /// </returns>
    IReadOnlyList<DeviceSnapshot> GetDeviceSnapshots();
}
