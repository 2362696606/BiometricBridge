using System.Text.Json.Serialization;

namespace BiometricBridge.Host.Dto.Enum;

/// <summary>
/// 设备认证等级。
/// </summary>
/// <remarks>
/// 线格式上是 <c>"L0"</c> / <c>"L1"</c> 字面量，与成员名相同，故无需另标线格式名。
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CertificationLevel
{
    /// <summary>
    /// L0：无安全存储的通用设备。
    /// </summary>
    L0,
    /// <summary>
    /// L1：带安全存储、可持有设备密钥的设备。
    /// </summary>
    L1,
}