using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace BiometricBridge.Host.Dto.Enum;

/// <summary>
/// 设备子类型。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceSubType
{
    /// <summary>四指联采。</summary>
    Slap,

    /// <summary>非接触采集。</summary>
    Touchless,

    /// <summary>单枚采集。</summary>
    Single,

    /// <summary>双枚采集。</summary>
    Double,

    /// <summary>全脸。</summary>
    [EnumMember(Value = "Full face")]
    [JsonStringEnumMemberName("Full face")]
    FullFace,

    /// <summary>
    /// 窄幅传感器上的单指采集。
    /// </summary>
    /// <remarks>
    /// 官方 schema 的子类型表里没有这个值，但实际设备在窄幅传感器上就报它。
    /// 枚举必须能表达它，否则序列化时会当场抛异常。
    /// </remarks>
    Finger
}
