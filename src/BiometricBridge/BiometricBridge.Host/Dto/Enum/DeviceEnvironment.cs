using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace BiometricBridge.Host.Dto.Enum;

/// <summary>
/// 采集所处的环境。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DeviceEnvironment
{
    /// <summary>预发布环境。</summary>
    Staging,

    /// <summary>开发环境。</summary>
    Developer,

    /// <summary>准生产环境。</summary>
    [EnumMember(Value = "Pre-Production")]
    [JsonStringEnumMemberName("Pre-Production")]
    PreProduction,

    /// <summary>生产环境。</summary>
    Production
}