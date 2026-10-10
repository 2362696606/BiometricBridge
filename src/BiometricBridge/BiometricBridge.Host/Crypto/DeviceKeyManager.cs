using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BiometricBridge.Host.Crypto;

/// <summary>
/// 设备密钥与证书材料的持有者：设备 RSA 私钥、设备证书，以及证书链 <c>[设备证书, DP 证书]</c>
/// </summary>
/// <remarks>
/// <para>
/// 只负责"保管与访问"（签名私钥、<c>x5c</c> 链），不负责签发（见 <see cref="IDeviceCertificateIssuer"/>）。
/// </para>
/// <para>
/// 设备密钥在构造时现场生成并请 DP 签发证书，全程在内存 —— 与参考实现一致：每次启动换一套，
/// 等价于密钥轮换，且不需要落盘保护私钥文件。
/// </para>
/// </remarks>
public sealed class DeviceKeyManager : IDisposable
{
    #region Fileds

    /// <summary>
    /// 设备 RSA 私钥
    /// </summary>
    private readonly RSA _deviceKey;

    /// <summary>
    /// 设备证书（叶子证书）
    /// </summary>
    private readonly X509Certificate2 _deviceCertificate;

    /// <summary>
    /// 是否已释放
    /// </summary>
    private bool _disposed;

    #endregion

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="deviceKey">
    /// 设备私钥
    /// </param>
    /// <param name="deviceCertificate">
    /// 设备证书
    /// </param>
    /// <param name="deviceProviderCertificate">
    /// DP 证书
    /// </param>
    private DeviceKeyManager(
        RSA deviceKey,
        X509Certificate2 deviceCertificate,
        X509Certificate2 deviceProviderCertificate)
    {
        _deviceKey = deviceKey;
        _deviceCertificate = deviceCertificate;
        CertificateChain = [deviceCertificate, deviceProviderCertificate];
    }

    #region Properties

    /// <summary>
    /// 设备私钥（供 RS256 签名）
    /// </summary>
    public RSA SigningKey => _deviceKey;

    /// <summary>
    /// 证书链，顺序为 <c>[设备证书, DP 证书]</c>
    /// </summary>
    public IReadOnlyList<X509Certificate2> CertificateChain { get; }

    #endregion

    /// <summary>
    /// 生成设备密钥并请 DP 签发设备证书
    /// </summary>
    /// <param name="issuer">
    /// 设备证书签发器
    /// </param>
    /// <param name="serialNo">
    /// 设备序列号
    /// </param>
    /// <param name="make">
    /// 厂商
    /// </param>
    /// <param name="model">
    /// 型号
    /// </param>
    /// <param name="deviceProvider">
    /// 设备供应商名称
    /// </param>
    /// <returns>
    /// 持有设备私钥、设备证书与链的管理器
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="issuer"/> 为 null
    /// </exception>
    public static DeviceKeyManager Create(
        IDeviceCertificateIssuer issuer,
        string serialNo,
        string make,
        string model,
        string deviceProvider)
    {
        ArgumentNullException.ThrowIfNull(issuer);

        var deviceKey = RSA.Create(2048);
        try
        {
            // 参考实现以 "<make>-<model>" 作为证书 Subject 的 OU。
            var deviceCertDer = issuer.Issue(deviceKey, serialNo, $"{make}-{model}", deviceProvider);
            var deviceCert = X509CertificateLoader.LoadCertificate(deviceCertDer);
            return new DeviceKeyManager(deviceKey, deviceCert, issuer.DeviceProviderCertificate);
        }
        catch
        {
            // 构造失败时把已生成的私钥就地收掉，别连它一起漏掉。
            deviceKey.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 取 JWS 头 <c>x5c</c> 所需的证书链
    /// </summary>
    /// <returns>
    /// 各证书 DER 的 base64 字符串，顺序同 <see cref="CertificateChain"/>
    /// </returns>
    /// <remarks>
    /// 用的是<b>标准 base64（含填充），不是 base64url</b> —— JWK 规范对 <c>x5c</c> 的约定。
    /// </remarks>
    public IReadOnlyList<string> GetX5C()
        => CertificateChain.Select(static certificate => Convert.ToBase64String(certificate.RawData)).ToArray();

    /// <summary>
    /// 释放设备私钥与设备证书
    /// </summary>
    /// <remarks>
    /// DP 证书归签发器所有（它要一直活在 <c>x5c</c> 链里），故不在此释放。
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _deviceCertificate.Dispose();
        _deviceKey.Dispose();
    }
}
