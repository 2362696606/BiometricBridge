using System.Text.Json;
using BiometricBridge.Host.Dto;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace BiometricBridge.Host.Crypto;

/// <summary>
/// <see cref="IJwsSigner"/> 的实现：用设备 RSA 私钥做 RS256 签名，JWS header 写入
/// <c>alg=RS256</c>、<c>typ=JWT</c> 与证书链 <c>x5c</c>（<c>[设备证书, DP 证书]</c>）
/// </summary>
/// <remarks>
/// 载荷先用 <see cref="SbiJson.Options"/> 序列化成字段字典再交给签名器：这样签名形态与未签名形态
/// （<see cref="UnsignedJwt"/>）用的是同一套字段名与取值，客户端比对两处时才不会"不是同一台设备"。
/// </remarks>
public sealed class JwsSigner : IJwsSigner
{
    #region Fileds

    /// <summary>
    /// 设备密钥与证书
    /// </summary>
    private readonly DeviceKeyManager _keyManager;

    /// <summary>
    /// JWT 处理器
    /// </summary>
    /// <remarks>
    /// <b>关闭默认时间声明</b>：置 <c>false</c> 后不再自动写入 <c>exp</c>/<c>iat</c>/<c>nbf</c>。
    /// 规范的 digitalId 与 deviceInfo 载荷只定义业务字段，多出来的声明既不符合字段集，
    /// 又可能被客户端当作有效期语义解读。
    /// </remarks>
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    #endregion

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="keyManager">
    /// 设备密钥与证书
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="keyManager"/> 为 null
    /// </exception>
    public JwsSigner(DeviceKeyManager keyManager)
    {
        ArgumentNullException.ThrowIfNull(keyManager);

        _keyManager = keyManager;
    }

    /// <inheritdoc/>
    public string SignDigitalId(DigitalId digitalId) => Sign(digitalId);

    /// <inheritdoc/>
    public string SignDeviceInfo(DeviceInfo deviceInfo) => Sign(deviceInfo);

    /// <inheritdoc/>
    public string SignRegistrationCaptureData(RegistrationCaptureData data) => Sign(data);

    /// <summary>
    /// 把载荷序列化后用设备私钥签名
    /// </summary>
    /// <typeparam name="TPayload">
    /// 载荷类型
    /// </typeparam>
    /// <param name="payload">
    /// 载荷
    /// </param>
    /// <returns>
    /// JWS compact 串
    /// </returns>
    private string Sign<TPayload>(TPayload payload)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Claims = ToClaimDictionary(payload),
            SigningCredentials = new SigningCredentials(
                new RsaSecurityKey(_keyManager.SigningKey), SecurityAlgorithms.RsaSha256),
            AdditionalHeaderClaims = new Dictionary<string, object>
            {
                // x5c 是标准 base64（含填充）的证书链，顺序 [设备证书, DP 证书]。
                ["x5c"] = _keyManager.GetX5C(),
            },
            TokenType = "JWT",
        };

        return _handler.CreateToken(descriptor);
    }

    /// <summary>
    /// 把载荷记录转成 JWT claim 字典
    /// </summary>
    /// <typeparam name="TPayload">
    /// 载荷类型
    /// </typeparam>
    /// <param name="payload">
    /// 载荷
    /// </param>
    /// <returns>
    /// claim 字典（保持序列化后的字段顺序）
    /// </returns>
    private static Dictionary<string, object> ToClaimDictionary<TPayload>(TPayload payload)
    {
        var json = JsonSerializer.Serialize(payload, SbiJson.Options);
        return JsonSerializer.Deserialize<Dictionary<string, object>>(json, SbiJson.Options)
            ?? throw new InvalidOperationException("载荷序列化失败。");
    }
}
