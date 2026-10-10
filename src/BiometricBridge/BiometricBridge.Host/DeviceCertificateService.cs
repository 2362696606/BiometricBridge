using BiometricBridge.Core;
using BiometricBridge.Host.Crypto;

namespace BiometricBridge.Host;

/// <summary>
/// 设备证书：对外服务签名所用的设备证书的管理入口
/// </summary>
/// <remarks>
/// <para>
/// 换证书是规范要求的一项操作（CTK 的 SBI1022 就要求操作员在两步之间换一套），但它作用在对外服务的
/// 签名材料上，故由本类出面：界面只认"设备证书"这一个概念，不必知道底下的 JWS 与 DP 材料。
/// </para>
/// <para>
/// 身份取自设备清单而非调用方传入：证书 Subject 里的厂商/型号/序列号必须与设备实际身份一致，
/// 由调用方拼这几个值迟早会拼错一个。
/// </para>
/// </remarks>
public sealed class DeviceCertificateService(IDeviceInventory inventory, DeviceSignerProvider signers)
{
    #region Fileds

    /// <summary>
    /// 设备清单
    /// </summary>
    private readonly IDeviceInventory _inventory = inventory;

    /// <summary>
    /// 签名器提供者
    /// </summary>
    private readonly DeviceSignerProvider _signers = signers;

    #endregion

    /// <summary>
    /// 更换指定设备的证书
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id
    /// </param>
    /// <exception cref="KeyNotFoundException">
    /// 设备 id 不存在
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 设备缺少静态信息，或尚未报出序列号（未连接）—— 设备证书的 Subject 里带着序列号，
    /// 也就必须先把设备连上
    /// </exception>
    public void Rotate(Guid deviceId)
    {
        var snapshot = _inventory.GetDeviceSnapshots().FirstOrDefault(s => s.DeviceId == deviceId)
            ?? throw new KeyNotFoundException($"未找到设备 {deviceId}。");

        if (snapshot.Info is not { } deviceInfo)
        {
            throw new InvalidOperationException($"设备 {deviceId} 缺少静态信息，无法签发设备证书。");
        }

        _signers.Rotate(
            deviceInfo.SerialNo ?? string.Empty,
            deviceInfo.Make,
            deviceInfo.Model,
            deviceInfo.DeviceProvider);
    }
}
