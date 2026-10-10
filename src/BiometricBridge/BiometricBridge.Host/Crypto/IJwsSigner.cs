using BiometricBridge.Host.Dto;

namespace BiometricBridge.Host.Crypto;

/// <summary>
/// JWS 签名：用设备私钥把 <c>digitalId</c> / <c>deviceInfo</c> 载荷签成 compact 串（RS256，header 含 x5c）
/// </summary>
/// <remarks>
/// 只负责"签名"，不负责密钥与证书的寿命（见 <see cref="DeviceKeyManager"/>）。
/// </remarks>
public interface IJwsSigner
{
    /// <summary>
    /// 签名 digitalId 载荷
    /// </summary>
    /// <param name="digitalId">
    /// 载荷
    /// </param>
    /// <returns>
    /// 形如 <c>base64url(header).base64url(payload).base64url(signature)</c> 的串
    /// </returns>
    string SignDigitalId(DigitalId digitalId);

    /// <summary>
    /// 签名 deviceInfo 载荷（其中内嵌的 digitalId 已由调用方先签好）
    /// </summary>
    /// <param name="deviceInfo">
    /// 载荷
    /// </param>
    /// <returns>
    /// JWS compact 串
    /// </returns>
    string SignDeviceInfo(DeviceInfo deviceInfo);

    /// <summary>
    /// 签名注册采集的数据载荷（其中内嵌的 digitalId 已由调用方先签好）
    /// </summary>
    /// <param name="data">
    /// 载荷
    /// </param>
    /// <returns>
    /// JWS compact 串
    /// </returns>
    /// <remarks>
    /// 规范要求采集响应里的 <c>data</c> 块本身也是一段 JWS，用同一把设备私钥签。
    /// </remarks>
    string SignRegistrationCaptureData(RegistrationCaptureData data);
}
