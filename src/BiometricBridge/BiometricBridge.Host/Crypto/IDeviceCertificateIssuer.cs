using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace BiometricBridge.Host.Crypto;

/// <summary>
/// 设备证书签发器：用设备供应商（DP）的材料给设备公钥签发一张设备证书
/// </summary>
/// <remarks>
/// 只负责"签发"，不负责持有设备私钥（见 <see cref="DeviceKeyManager"/>）。拆成两个类型，是因为
/// 两种材料的来源与寿命完全不同：设备密钥每次启动新生成，DP 材料是部署时给定的。
/// </remarks>
public interface IDeviceCertificateIssuer
{
    /// <summary>
    /// DP 自己的证书
    /// </summary>
    /// <remarks>
    /// 它要进 JWS 头的 <c>x5c</c> 链，故签发器须把原件留着、且在用链的期间不能被释放。
    /// </remarks>
    X509Certificate2 DeviceProviderCertificate { get; }

    /// <summary>
    /// 给设备公钥签发设备证书
    /// </summary>
    /// <param name="deviceKey">
    /// 设备公钥（私钥留在设备侧，本方法只取公钥）
    /// </param>
    /// <param name="serialNo">
    /// 设备序列号，进证书 Subject 的 <c>CN</c>
    /// </param>
    /// <param name="makeModel">
    /// 厂商与型号，进证书 Subject 的 <c>OU</c>
    /// </param>
    /// <param name="deviceProvider">
    /// 设备供应商名称，进证书 Subject 的 <c>O</c>
    /// </param>
    /// <returns>
    /// 设备证书的 DER 字节
    /// </returns>
    byte[] Issue(RSA deviceKey, string serialNo, string makeModel, string deviceProvider);
}
