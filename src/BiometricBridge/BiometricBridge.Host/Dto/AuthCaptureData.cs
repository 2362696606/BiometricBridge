using BiometricBridge.Host.Dto.Enum;

namespace BiometricBridge.Host.Dto;

/// <summary>
/// 认证采集响应里 <c>data</c> 块 JWS 的载荷。
/// </summary>
/// <remarks>
/// <para>
/// 与注册侧的同名载荷相比多一项 <see cref="DomainUri"/>：认证采集要指明生物特征送往哪个
/// 认证服务器，注册采集不涉及。
/// </para>
/// <para>
/// 本载荷是<b>被签名</b>的对象，不是报文的顶层：外面还套着
/// <see cref="AuthCaptureResponseBiometric"/>。
/// </para>
/// </remarks>
public sealed record AuthCaptureData
{
    /// <summary>数字标识，本处是<b>已签名</b>的。</summary>
    public required string DigitalId { get; init; }

    /// <summary>设备供应方生成的设备代码。</summary>
    public required string DeviceCode { get; init; }

    /// <summary>本服务的版本。</summary>
    public required string DeviceServiceVersion { get; init; }

    /// <summary>本枚生物特征的模态。</summary>
    public required BiometricType BioType { get; init; }

    /// <summary>本枚生物特征的具体部位。</summary>
    public required string BioSubType { get; init; }

    /// <summary>本次采集的用途。</summary>
    public required Purpose Purpose { get; init; }

    /// <summary>采集所处的环境。</summary>
    public required DeviceEnvironment Env { get; init; }

    /// <summary>认证服务器的地址。注册采集没有这一项。</summary>
    public required string DomainUri { get; init; }

    /// <summary>本枚生物特征的数据。</summary>
    public required string BioValue { get; init; }

    /// <summary>本次采集的事务号。</summary>
    public required string TransactionId { get; init; }

    /// <summary>采集时刻。</summary>
    public required string Timestamp { get; init; }

    /// <summary>请求方要求的最低分数。</summary>
    /// <remarks>
    /// <para>
    /// <b>必现</b>：schema 把它列在 required 里，请求方没给时也要报一个
    /// （见 <see cref="SbiScore.Unspecified"/>）。
    /// </para>
    /// <para>
    /// 线格式与 <see cref="QualityScore"/> 一样，是数值形态的字符串 —— 见 <see cref="SbiScore"/>。
    /// </para>
    /// </remarks>
    public required SbiScore RequestedScore { get; init; }

    /// <summary>本次采集得到的质量分。</summary>
    /// <remarks>
    /// 线格式是数值形态的字符串 —— 见 <see cref="SbiScore"/>。
    /// </remarks>
    public required SbiScore QualityScore { get; init; }
}
