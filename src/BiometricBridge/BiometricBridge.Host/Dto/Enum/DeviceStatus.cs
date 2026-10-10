using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace BiometricBridge.Host.Dto.Enum;

/// <summary>
/// 设备状态。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceStatus
{
    /// <summary>就绪，可接受采集。</summary>
    Ready,

    /// <summary>忙碌，正在处理上一次采集。</summary>
    Busy,

    /// <summary>未就绪。</summary>
    [EnumMember(Value = "Not Ready")] [JsonStringEnumMemberName("Not Ready")]
    NotReady,

    /// <summary>尚未注册。</summary>
    [EnumMember(Value = "Not Registered")] [JsonStringEnumMemberName("Not Registered")]
    NotRegistered
}