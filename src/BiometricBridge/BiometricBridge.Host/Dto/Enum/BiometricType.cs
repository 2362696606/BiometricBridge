using System.Text.Json.Serialization;

namespace BiometricBridge.Host.Dto.Enum;

/// <summary>
/// 生物特征模态。
/// </summary>
/// <remarks>
/// 线格式上是 <c>"Finger"</c> / <c>"Iris"</c> / <c>"Face"</c> 字面量，与成员名相同，故无需另标线格式名。
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BiometricType
{
    /// <summary>指纹。</summary>
    Finger,

    /// <summary>虹膜。</summary>
    Iris,

    /// <summary>人脸。</summary>
    Face
}