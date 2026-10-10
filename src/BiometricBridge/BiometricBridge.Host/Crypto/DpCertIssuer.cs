using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BiometricBridge.Host.Crypto;

/// <summary>
/// 用设备供应商（DP）材料签发设备证书：加载 DP 证书与 PKCS#8 私钥，给设备公钥签发一张设备证书
/// </summary>
/// <remarks>
/// <para>
/// 证书字段对齐参考实现：Subject 为
/// <c>C=CN, ST=Guangdong, L=Shenzhen, O=&lt;deviceProvider&gt;, OU=&lt;makeModel&gt;, CN=&lt;serialNo&gt;</c>；
/// 有效期 90 天、起点回溯 5 分钟（容忍时钟偏差）；<c>BasicConstraints</c> CA=false；
/// <c>KeyUsage</c>=digitalSignature|keyEncipherment；SKI=设备公钥；AKI=DP 的 keyIdentifier。
/// </para>
/// <para>
/// <b>本类型持有 DP 私钥，寿命与进程相同、不可随手释放</b>：JWS 头的 <c>x5c</c> 链里含 DP 证书，
/// 释放它会让正在签名的链路失效（<c>ObjectDisposedException</c>）。
/// </para>
/// </remarks>
public sealed class DpCertIssuer : IDeviceCertificateIssuer, IDisposable
{
    #region 常量

    /// <summary>
    /// 设备证书有效期（天）
    /// </summary>
    /// <remarks>
    /// 取参考实现的 90 天：DP 证书一年，留出余量。
    /// </remarks>
    private const int DeviceCertificateValidityDays = 90;

    /// <summary>
    /// 有效期起始的回拨量，容忍设备与主机的时钟偏差
    /// </summary>
    private static readonly TimeSpan ClockSkewTolerance = TimeSpan.FromMinutes(5);

    #endregion

    #region Fileds

    /// <summary>
    /// DP 私钥
    /// </summary>
    private readonly RSA _providerKey;

    /// <summary>
    /// 时间提供者，用于设备证书的有效期
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// 是否已释放
    /// </summary>
    private bool _disposed;

    #endregion

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="providerDirectory">
    /// DP 材料所在目录（须为绝对路径，由组合根解析）
    /// </param>
    /// <param name="certificateFileName">
    /// DP 证书文件名（DER 或 PEM）
    /// </param>
    /// <param name="privateKeyFileName">
    /// DP 私钥文件名（PKCS#8 PEM，可加密）
    /// </param>
    /// <param name="privateKeyPassword">
    /// DP 私钥口令；未加密的 PEM 可传空
    /// </param>
    /// <param name="timeProvider">
    /// 时间提供者
    /// </param>
    /// <exception cref="FileNotFoundException">
    /// 证书或私钥文件不存在
    /// </exception>
    /// <exception cref="CryptographicException">
    /// 私钥口令错误或格式非法
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// DP 私钥与证书不匹配
    /// </exception>
    public DpCertIssuer(
        string providerDirectory,
        string certificateFileName,
        string privateKeyFileName,
        string privateKeyPassword,
        TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(certificateFileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyFileName);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _timeProvider = timeProvider;

        var certificatePath = Path.Combine(providerDirectory, certificateFileName);
        if (!File.Exists(certificatePath))
        {
            throw new FileNotFoundException($"DP 证书不存在：{certificatePath}", certificatePath);
        }

        var privateKeyPath = Path.Combine(providerDirectory, privateKeyFileName);
        if (!File.Exists(privateKeyPath))
        {
            throw new FileNotFoundException($"DP 私钥不存在：{privateKeyPath}", privateKeyPath);
        }

        DeviceProviderCertificate = X509CertificateLoader.LoadCertificateFromFile(certificatePath);
        _providerKey = LoadPrivateKey(File.ReadAllText(privateKeyPath), privateKeyPassword);
        EnsureKeyMatchesCertificate();
    }

    /// <inheritdoc/>
    public X509Certificate2 DeviceProviderCertificate { get; }

    /// <inheritdoc/>
    public byte[] Issue(RSA deviceKey, string serialNo, string makeModel, string deviceProvider)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(deviceKey);

        var subject = new X500DistinguishedName(
            $"C=CN, ST=Guangdong, L=Shenzhen, O={deviceProvider}, OU={makeModel}, CN={serialNo}");
        var request = new CertificateRequest(subject, deviceKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        // AKI 只取 keyIdentifier（即 DP 证书的 SKI），不含 issuer/serial —— 与参考实现一致。
        request.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(DeviceProviderCertificate, true, false));

        // Issuer 名称按 DP 证书 Subject 的原始编码保留：RDN 顺序或别名一旦被重新编码，链就验不过。
        var issuerName = new X500DistinguishedName(DeviceProviderCertificate.SubjectName.RawData);
        var notBefore = _timeProvider.GetUtcNow() - ClockSkewTolerance;
        var notAfter = notBefore.AddDays(DeviceCertificateValidityDays);
        var generator = X509SignatureGenerator.CreateForRSA(_providerKey, RSASignaturePadding.Pkcs1);

        using var certificate = request.Create(issuerName, generator, notBefore, notAfter, CreateSerialNumber());
        return certificate.RawData;
    }

    /// <summary>
    /// 释放 DP 私钥与 DP 证书
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DeviceProviderCertificate.Dispose();
        _providerKey.Dispose();
    }

    /// <summary>
    /// 生成一个正的随机证书序列号（9 字节，最高位清零）
    /// </summary>
    /// <returns>
    /// 证书序列号
    /// </returns>
    private static byte[] CreateSerialNumber()
    {
        var serialNumber = RandomNumberGenerator.GetBytes(9);
        serialNumber[0] &= 0x7F;
        return serialNumber;
    }

    /// <summary>
    /// 加载 PKCS#8 私钥 PEM（加密或未加密）
    /// </summary>
    /// <param name="pem">
    /// PEM 文本
    /// </param>
    /// <param name="password">
    /// 口令
    /// </param>
    /// <returns>
    /// RSA 私钥
    /// </returns>
    private static RSA LoadPrivateKey(string pem, string password)
    {
        var rsa = RSA.Create();
        try
        {
            if (pem.Contains("ENCRYPTED PRIVATE KEY", StringComparison.Ordinal))
            {
                rsa.ImportFromEncryptedPem(pem, password);
            }
            else
            {
                rsa.ImportFromPem(pem);
            }

            return rsa;
        }
        catch
        {
            rsa.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 校验私钥与证书公钥一致
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// 证书公钥不是 RSA，或私钥与证书不匹配
    /// </exception>
    /// <remarks>
    /// 张冠李戴时签出来的证书验不过链，而那时离出错点已经很远，故在这里当场拦住。
    /// </remarks>
    private void EnsureKeyMatchesCertificate()
    {
        using var certificateKey = DeviceProviderCertificate.GetRSAPublicKey();
        if (certificateKey is null)
        {
            throw new InvalidOperationException("DP 证书公钥不是 RSA，无法用于签发。");
        }

        var certParameters = certificateKey.ExportParameters(false);
        var keyParameters = _providerKey.ExportParameters(false);
        var matches = certParameters.Modulus.AsSpan().SequenceEqual(keyParameters.Modulus)
            && certParameters.Exponent.AsSpan().SequenceEqual(keyParameters.Exponent);

        if (!matches)
        {
            throw new InvalidOperationException("DP 私钥与 DP 证书不匹配。");
        }
    }
}
