using System.Text.Json.Serialization;
using BiometricBridge.Host.Dto.Enum;

namespace BiometricBridge.Host.Dto;

/// <summary>
/// 设备信息载荷：<c>/info</c> 响应里那串 JWS 解出来的内容。
/// </summary>
/// <remarks>
/// <para>
/// 与同名的 <see cref="DeviceInfoResponse"/> 不同层：那个的 <c>deviceInfo</c> 属性承载的是本结构
/// 序列化并签名后的<b>文本</b>，本类型是那段文本解开后的<b>内容</b>。
/// </para>
/// <para>
/// 与设备发现响应的区别在于：发现响应把各项平铺在数组元素上，这里则整体塞进一串 JWS。
/// </para>
/// </remarks>
public sealed record DeviceInfo
{
    /// <summary>设备标识。</summary>
    public required string DeviceId { get; init; }

    /// <summary>设备当前状态。</summary>
    public required DeviceStatus DeviceStatus { get; init; }

    /// <summary>固件版本。</summary>
    public required string Firmware { get; init; }

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

    /// <summary>数字标识，本处是<b>已签名</b>的。</summary>
    /// <remarks>
    /// 内容是一个 JSON 串，其结构见 <see cref="Dto.DigitalId"/>——但此处承载的是已序列化的文本。
    /// </remarks>
    public required string DigitalId { get; init; }

    /// <summary>设备供应方生成的设备代码。</summary>
    public required string DeviceCode { get; init; }

    /// <summary>设备所处的环境。</summary>
    public required DeviceEnvironment Env { get; init; }

    /// <summary>本服务支持的 SBI 版本。</summary>
    public required string[] SpecVersion { get; init; }

    /// <summary>本设备的用途：<c>Auth</c> / <c>Registration</c>；未注册时为空串。</summary>
    /// <remarks>
    /// <b>类型是 string 而不是枚举</b>：未注册设备的用途必须是<b>空串</b>（规范原文 "For devices that are
    /// not registered the purpose is empty."），枚举表达不了"空"。
    /// </remarks>
    public required string Purpose { get; init; }

    /// <summary>失败原因。</summary>
    /// <remarks>
    /// <b>可空，且正常时就是不出现</b>：本载荷是随签名一起交付的设备自述，成功时没有要报的错误。
    /// 这与设备发现响应不同——那里协议把错误对象列为结构的一部分，成功时也填。
    /// <para>
    /// 「不出现」是<b>略去这个键</b>，不是写成 <c>null</c>——协议给本字段定的是对象类型，
    /// 而序列化器默认会把 <see langword="null"/> 照写成 <c>"error": null</c>，故这里显式声明。
    /// </para>
    /// </remarks>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SbiError? Error { get; init; }
}
