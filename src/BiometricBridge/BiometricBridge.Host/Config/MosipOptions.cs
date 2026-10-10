namespace BiometricBridge.Host.Config;

/// <summary>
/// MOSIP 侧的部署设置：设备供应商（DP）材料的位置，以及报文里的环境取值
/// </summary>
/// <remarks>
/// 只承载"这台机器上部署成什么样"的事实，不含设备自身的信息（那些在设备特性上）。
/// </remarks>
public sealed class MosipOptions
{
    /// <summary>
    /// DP 材料所在目录（相对或绝对路径）
    /// </summary>
    /// <remarks>
    /// 相对路径由 <see cref="SbiMaterialDirectory"/> 解析：先按工作目录、再按应用输出目录 ——
    /// `dotnet run` 与 IDE 启动的工作目录不同，只认一个会在其中一种启动方式下找不到材料，
    /// 而失败发生在首次签名时，表现为与"设备"毫无关系的 500，极难定位。
    /// </remarks>
    public string DeviceProviderPath { get; init; } = "device-provider";

    /// <summary>
    /// DP 证书文件名（DER 或 PEM）
    /// </summary>
    public string DeviceProviderCertFile { get; init; } = "ict_device_provider.cert.der";

    /// <summary>
    /// DP 私钥文件名（PKCS#8 PEM，可加密）
    /// </summary>
    public string DeviceProviderKeyFile { get; init; } = "ict_device_provider.key.pem";

    /// <summary>
    /// DP 私钥口令
    /// </summary>
    /// <remarks>
    /// 随 CTK 测试材料一起来的默认值；正式部署时由配置覆盖。
    /// </remarks>
    public string DeviceProviderKeyPassword { get; init; } = "123";

    /// <summary>
    /// 报文里 <c>env</c> 的取值，取 <c>DeviceEnvironment</c> 的成员名
    /// </summary>
    /// <remarks>
    /// schema 限定为 Staging / Developer / Pre-Production / Production 四者之一。
    /// </remarks>
    public string Env { get; init; } = "Staging";
}
