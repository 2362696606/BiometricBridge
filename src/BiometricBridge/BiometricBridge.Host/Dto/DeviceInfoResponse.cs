using System.Text.Json.Serialization;

namespace BiometricBridge.Host.Dto;

/// <summary>
/// 设备信息响应（<c>MOSIPDINFO /info</c>）里的一个元素。
/// </summary>
/// <remarks>
/// <para>
/// <b>本端点的报文是数组</b>，本类型是数组元素的形状，不是整体。
/// </para>
/// <para>
/// 元素只有两项：设备信息本身是一串 JWS，不是结构化的对象。已注册的设备是
/// <c>header.payload.signature</c> 三段，未注册的只有 <c>payload</c> 一段。
/// <see cref="DeviceInfo"/> 承载的就是这串文本，解出来的结构见 <see cref="DeviceInfo"/> 类型
/// ——同名但不同层：这里是串，那里是串解开后的内容。
/// </para>
/// <para>
/// 两项在协议里都标为必填，<b>连 <see cref="Error"/> 也是</b>——成功报文里它同样在，
/// 取表示成功的那个码。
/// </para>
/// </remarks>
public sealed record DeviceInfoResponse
{
    /// <summary>设备的 JWS 串（未注册设备为未签名的 base64url 串）。</summary>
    /// <remarks>
    /// <b>必填</b>：CTK 的 <c>DeviceInfo*ResponseSchema</c> 除"未注册"那份外都把它列为必填，
    /// 且"只有 error、不给 deviceInfo"的元素形状在任何一份里都不合法 —— 故给不出它的设备
    /// 干脆不产出元素，而不是产出半个。
    /// </remarks>
    public required string DeviceInfo { get; init; }

    /// <summary>失败原因；成功时也必须有，取表示成功的那个码。</summary>
    public required SbiError Error { get; init; }
}
