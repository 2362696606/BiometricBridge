using System.Text.Json.Serialization;
using BiometricBridge.Host.Dto.Enum;

namespace BiometricBridge.Host.Dto;

/// <summary>
/// 认证采集请求里对某一模态的采集要求，即 <c>bio[]</c> 的一个元素。
/// </summary>
/// <remarks>
/// <para>
/// 与注册侧的同名元素相比，本类型<b>没有</b>「例外部位」那一项：
/// 「某枚缺失且在列时，为那一枚回填而不是整次失败」是注册采集才有的语义。
/// </para>
/// </remarks>
public sealed record AuthCaptureRequestBio
{
    /// <summary>要采集的生物特征模态。</summary>
    public BiometricType? Type { get; init; }

    /// <summary>要采集的枚数。</summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? Count { get; init; }

    /// <summary>要采集的具体部位，如 <c>Left Thumb</c>。</summary>
    public string[]? BioSubType { get; init; }

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
