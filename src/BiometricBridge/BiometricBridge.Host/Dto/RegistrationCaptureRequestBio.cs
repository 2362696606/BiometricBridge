using System.Text.Json.Serialization;
using BiometricBridge.Host.Dto.Enum;

namespace BiometricBridge.Host.Dto;

/// <summary>
/// 注册采集请求里对某一模态的采集要求，即 <c>bio[]</c> 的一个元素。
/// </summary>
/// <remarks>
/// <para>
/// 比认证侧的同名元素多一项 <see cref="Exception"/>：某枚缺失而该部位又在
/// <see cref="BioSubType"/> 里列着时，为那一枚回填而不是让整次采集失败。
/// </para>
/// <para>
/// <b>不认未知属性</b>，理由同 <see cref="RegistrationCaptureRequest"/>。CTK 的 SBI1099 把
/// <c>bio[0].count</c> 改名成 <c>countXXX</c>，指的就是这一层。
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegistrationCaptureRequestBio
{
    /// <summary>要采集的生物特征模态。</summary>
    public BiometricType? Type { get; init; }

    /// <summary>要采集的枚数。</summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? Count { get; init; }

    /// <summary>要采集的具体部位，如 <c>Left Thumb</c>。</summary>
    public string[]? BioSubType { get; init; }

    /// <summary>本次允许缺失的部位。缺失的这几枚回填，不算整次失败。</summary>
    public string[]? Exception { get; init; }

    /// <summary>本次采集要求的最低分数。</summary>
    /// <remarks>
    /// <b>本字段在请求的线格式上是字符串</b>（内容是数值），响应载荷里的同名字段同型
    /// —— 见 <see cref="SbiScore"/>。
    /// </remarks>
    public string? RequestedScore { get; init; }

    /// <summary>要使用的设备。</summary>
    public string? DeviceId { get; init; }

    /// <summary>要使用的设备子 ID。</summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? DeviceSubId { get; init; }

    /// <summary>上一数据块的哈希。</summary>
    public string? PreviousHash { get; init; }
}
