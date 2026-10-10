namespace BiometricBridge.Host.Dto;

/// <summary>
/// 设备发现请求（<c>MOSIPDISC /device</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 报文体只有一项：要找哪一类设备。故而这是一个单字段的请求。
/// </para>
/// <para>
/// <b>类型是 string 而不是枚举</b>：非法取值（大写、拼错、缺失）必须能<b>走进来处理</b>，好按协议回
/// "发现响应的错误态"（见 <see cref="DiscoveryErrorResponse"/>）。用枚举的话序列化器会在入口当场把
/// 请求判为坏报文、回 400 —— 而协议要的是 <b>200 + 错误元素</b>，CTK 的 SBI1196
/// （Discover request attributes in UPPER CASE）正是这条负向用例。
/// </para>
/// <para>
/// 属性可空：缺失也是非法取值之一，由处理者按同一套规则回错误态。规范把该字段列为必填。
/// </para>
/// </remarks>
public sealed record DiscoveryRequest
{
    /// <summary>要发现的设备类型：<c>Biometric Device</c> / <c>Finger</c> / <c>Iris</c> / <c>Face</c>。</summary>
    public string? Type { get; init; }
}
