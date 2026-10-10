using BiometricBridge.Host.Dto.Enum;

namespace BiometricBridge.Host.Dto;

/// <summary>
/// 设备发现响应（<c>MOSIPDISC /device</c>）里的一条设备记录
/// </summary>
/// <remarks>
/// <para>
/// <b>本端点的报文是数组</b>，本类型是「正向」元素的形状；失败另有一种形状，见
/// <see cref="DiscoveryErrorResponse"/> —— 协议为两者各定了一份 schema。
/// </para>
/// <para>
/// 各项都 <c>required</c>：出站报文缺字段就是坏报文，用编译器强制装配时填全。
/// </para>
/// <para>
/// <b><see cref="Error"/> 必填且就绪时取 <c>{"0","Success"}</c></b>：CTK 的
/// <c>DiscoverResponseSchema</c> 把 <c>error</c> 列进了元素的 <c>required</c>，且把
/// <c>errorCode</c>/<c>errorInfo</c> 钉成固定 enum <c>["0"]</c>/<c>["Success"]</c> ——
/// 2026-10-09 实测：就绪时省掉它会被判 <c>$[0].error: is missing but it is required</c>。
/// （三位非零开头那条约束属于另一份 schema，见 <see cref="DiscoveryErrorResponse"/>。）
/// </para>
/// </remarks>
public sealed record DiscoveryResponse
{
    /// <summary>设备标识。</summary>
    public required string DeviceId { get; init; }

    /// <summary>设备当前状态。</summary>
    public required DeviceStatus DeviceStatus { get; init; }

    /// <summary>设备认证等级。</summary>
    public required CertificationLevel Certification { get; init; }

    /// <summary>本服务的版本。</summary>
    public required string ServiceVersion { get; init; }

    /// <summary>本设备支持的设备子 ID。</summary>
    /// <remarks>
    /// <b>线格式是字符串数组</b>（取值 <c>"0"</c>–<c>"3"</c>）：schema 文档写作 integer，
    /// 但 CTK 实际用的 schema 期望字符串，发整数会被判 <c>integer found, string expected</c>。
    /// </remarks>
    public required string[] DeviceSubId { get; init; }

    /// <summary>到达本服务的基地址。</summary>
    public required string CallbackId { get; init; }

    /// <summary>数字标识。发现端点上它是<b>未签名</b>的。</summary>
    /// <remarks>
    /// 内容是一个 JSON 串，其结构见 <see cref="DigitalId"/>——但此处承载的是已序列化的文本，
    /// 不经本类型反序列化。
    /// </remarks>
    public required string DigitalId { get; init; }

    /// <summary>设备供应方生成的设备代码。</summary>
    public required string DeviceCode { get; init; }

    /// <summary>本服务支持的 SBI 版本。</summary>
    public required string[] SpecVersion { get; init; }

    /// <summary>本设备的用途：<c>Auth</c> / <c>Registration</c>；未注册时为空串。</summary>
    /// <remarks>
    /// <b>类型是 string 而不是枚举</b>：未注册设备的用途必须是<b>空串</b>（规范原文 "For devices that are
    /// not registered the purpose is empty."），枚举表达不了"空"。
    /// </remarks>
    public required string Purpose { get; init; }

    /// <summary>失败原因；就绪时取表示成功的那个码。</summary>
    public required SbiError Error { get; init; }
}
