using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace BiometricBridge.Host.Dto;

/// <summary>
/// 未签名形态的编码：<c>base64url(载荷 JSON)</c> —— 既没有 header 段，也没有签名段
/// </summary>
/// <remarks>
/// <para>
/// 规范规定了两处使用未签名形态，都不是"省事"，而是协议要求：
/// </para>
/// <list type="bullet">
/// <item>
/// <c>/device</c> 的 <c>digitalId</c> —— 发现发生在信任建立之前，此时还没有可用于验签的锚点。
/// </item>
/// <item>
/// <b>未注册设备</b>的 <c>/info</c> —— "For a device which is not registered, the deviceInfo will be unsigned."
/// </item>
/// </list>
/// <para>
/// 与已签名形态共用 <see cref="SbiJson.Options"/>。
/// </para>
/// </remarks>
public static class UnsignedJwt
{
    /// <summary>
    /// 把载荷编码为未签名形态
    /// </summary>
    /// <typeparam name="TPayload">
    /// 载荷类型
    /// </typeparam>
    /// <param name="payload">
    /// 要编码的载荷
    /// </param>
    /// <returns>
    /// <c>base64url(JSON)</c> 字符串。不含点号分隔符，故不可能被误当成 JWS compact
    /// </returns>
    public static string Encode<TPayload>(TPayload payload)
        => Base64UrlEncoder.Encode(JsonSerializer.Serialize(payload, SbiJson.Options));
}
