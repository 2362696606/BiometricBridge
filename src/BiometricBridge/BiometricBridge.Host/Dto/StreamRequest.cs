using System.Text.Json.Serialization;

namespace BiometricBridge.Host.Dto;

/// <summary>
/// 预览推流请求（<c>STREAM /stream</c>）。
/// </summary>
/// <remarks>
/// 只有三项，且都用于定位设备与限定推流时长——推流本身不带任何生物特征参数。
/// </remarks>
public sealed record StreamRequest
{
    /// <summary>要推流的设备。</summary>
    public string? DeviceId { get; init; }

    /// <summary>要推流的设备子 ID。</summary>
    /// <remarks>
    /// CTK 实测把 <c>deviceSubId</c> 写成字符串（<c>"3"</c>），故标 <see cref="JsonNumberHandling"/> 让
    /// <c>"3"</c> 也能读入；字段类型仍是数值，保持契约语义。
    /// </remarks>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? DeviceSubId { get; init; }

    /// <summary>推流时长（毫秒）。</summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? Timeout { get; init; }
}
