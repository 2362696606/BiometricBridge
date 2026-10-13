using BiometricBridge.Core.Models;

namespace BiometricBridge.Host;

/// <summary>
/// 设备在 SBI 报文里的身份：序列号
/// </summary>
/// <remarks>
/// <para>
/// 设备<b>当前</b>报出的序列号优先，并顺手记下；当前报不出（未连接）就用最近一次见到的。
/// </para>
/// <para>
/// <b>为什么必须记住</b>：序列号只有连着的时候才由设备报出，而它同时是发现与设备信息两个端点里
/// <c>deviceId</c>、<c>deviceCode</c> 与内嵌 <c>serialNo</c> 的取值，三处都要求非空
/// （CTK 的 schema 是 <c>non-empty-string</c>）。而"把设备拔掉"恰是造 <c>Not Ready</c> 最常规的做法 ——
/// 不记它，那条路径就报不出合法元素。参考实现同样留了一份 lastKnownInfo。
/// </para>
/// <para>
/// <b>为什么两个端点共用一份，而不是各自缓存</b>：MOSIP 注册客户端拿到 <c>/info</c> 的 <c>deviceId</c> 后，
/// 会重新发一次发现请求、拿它去响应里逐字段比对（<c>isDeviceAvailable</c>：状态、认证等级、
/// <c>deviceCode</c> 与 <c>deviceId</c> 都要对上），对不上就把设备从它的注册表里摘掉、界面报
/// "Device is not available"。两端点各缓存一份，迟早会在"设备刚拔掉/刚插上"这类时刻分家。
/// </para>
/// </remarks>
public sealed class DeviceIdentityService
{
    /// <summary>
    /// 各设备最近一次报出的序列号
    /// </summary>
    private readonly Dictionary<Guid, string> _lastKnownSerialNumbers = [];

    /// <summary>
    /// 保护上面的字典（发现与设备信息两个端点都会被并发调用）
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// 取这台设备在报文里的身份
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id，即设备字典的键
    /// </param>
    /// <param name="deviceInfo">
    /// 设备静态信息
    /// </param>
    /// <returns>
    /// 序列号；这台设备从没报过则为空串（调用方据此略过它）
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="deviceInfo"/> 为 null
    /// </exception>
    public string Resolve(Guid deviceId, DeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        lock (_gate)
        {
            if (deviceInfo.SerialNo is { Length: > 0 } current)
            {
                _lastKnownSerialNumbers[deviceId] = current;
                return current;
            }

            return _lastKnownSerialNumbers.GetValueOrDefault(deviceId, string.Empty);
        }
    }
}
