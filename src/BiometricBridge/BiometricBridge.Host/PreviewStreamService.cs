using BiometricBridge.Core;
using CoreModels = BiometricBridge.Core.Models;

namespace BiometricBridge.Host;

/// <summary>
/// SBI 预览推流：按请求选定要推流的设备
/// </summary>
/// <remarks>
/// 只做"选哪台"这件事 —— 帧怎么编、怎么写是宿主（传输层）的事，故本类不引任何 HTTP 依赖。
/// </remarks>
public sealed class PreviewStreamService
{
    #region Fileds

    /// <summary>
    /// 设备清单
    /// </summary>
    private readonly IDeviceInventory _inventory;

    #endregion

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="inventory">
    /// 设备清单
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="inventory"/> 为 null
    /// </exception>
    public PreviewStreamService(IDeviceInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        _inventory = inventory;
    }

    /// <summary>
    /// 选出要推流的设备
    /// </summary>
    /// <param name="deviceId">
    /// 请求里的设备 id；为空表示不限
    /// </param>
    /// <param name="deviceSubId">
    /// 请求里的设备子 ID；为空表示不限
    /// </param>
    /// <returns>
    /// 选中的设备快照（含连接状态，供调用方判断）；选不出时返回 null
    /// </returns>
    /// <remarks>
    /// 给了设备 id 就按它精确匹配；否则按子 ID 匹配"支持该子 ID 的设备"；都没给则退到唯一那台设备，
    /// 多台时宁可不选 —— 推错设备的画面比报错更糟。
    /// </remarks>
    public CoreModels.DeviceSnapshot? ResolveDevice(string? deviceId, int? deviceSubId)
    {
        var usable = _inventory.GetDeviceSnapshots().Where(snapshot => snapshot.Info is not null).ToArray();

        if (!string.IsNullOrEmpty(deviceId))
        {
            return usable.FirstOrDefault(snapshot =>
                string.Equals(snapshot.DeviceId.ToString(), deviceId, StringComparison.OrdinalIgnoreCase));
        }

        if (deviceSubId is { } subId)
        {
            var bySubId = usable.FirstOrDefault(snapshot => snapshot.Info!.DeviceSubIds.Contains(subId));
            if (bySubId is not null)
            {
                return bySubId;
            }
        }

        return usable.Length == 1 ? usable[0] : null;
    }
}
