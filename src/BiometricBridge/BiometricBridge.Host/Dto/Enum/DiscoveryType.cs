using System.Runtime.Serialization;
using System.Text.Json.Serialization;

namespace BiometricBridge.Host.Dto.Enum;

/// <summary>
/// 设备发现请求里要发现的设备类型。
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiscoveryType
{
    /// <summary>指纹设备。</summary>
    Finger,

    /// <summary>人脸设备。</summary>
    Face,

    /// <summary>虹膜设备。</summary>
    Iris,

    /// <summary>不限模态的生物特征设备。</summary>
    [EnumMember(Value = "Biometric Device")]
    [JsonStringEnumMemberName("Biometric Device")]
    BiometricDevice
}