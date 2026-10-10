using System.Text.Json.Serialization;

namespace BiometricBridge.Host.Dto.Enum;

/// <summary>
/// 设备用途，也用于标识一次采集的用途。
/// </summary>
/// <remarks>
/// 线格式上是 <c>"Auth"</c> / <c>"Registration"</c> 字面量，与成员名相同，故无需另标线格式名。
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Purpose
{
    /// <summary>认证。</summary>
    Auth,

    /// <summary>注册。</summary>
    Registration
}