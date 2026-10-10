using System.Globalization;
using BiometricBridge.Host.Dto;
using CoreModels = BiometricBridge.Core.Models;

namespace BiometricBridge.Host;

/// <summary>
/// 按设备信息构造 <see cref="DigitalId"/> 载荷，供 <c>/device</c>（输出未签名形态）与 <c>/info</c>
/// （输出已签名形态）共用
/// </summary>
/// <remarks>
/// 集中到一处是必须的：同一个 digitalId 载荷在这两个端点各出现一次，客户端会<b>比对</b>两者是否一致
/// （据此确认"还是刚才那台设备"）。若两处各自拼字段，一次单边修改就会让比对失败。
/// </remarks>
internal static class SbiDigitalIdFactory
{
    /// <summary>
    /// 构造 digitalId 载荷
    /// </summary>
    /// <param name="deviceInfo">
    /// 设备静态信息
    /// </param>
    /// <param name="serialNo">
    /// 设备序列号
    /// </param>
    /// <returns>
    /// 字段齐全的 digitalId 载荷
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="deviceInfo"/> 为 null
    /// </exception>
    /// <remarks>
    /// 序列号由调用方给出，而不是就地读 <see cref="CoreModels.DeviceInfo.SerialNo"/>：设备只在连接期间报得出它，
    /// 而 <c>/info</c> 那边记得住"上次见过的序列号"（见该处说明），两处得用同一个值。
    /// 线格式要求该字段出现，取不到时用空串 —— 空串是有值的。
    /// </remarks>
    public static DigitalId Create(CoreModels.DeviceInfo deviceInfo, string serialNo)
    {
        ArgumentNullException.ThrowIfNull(deviceInfo);

        return new DigitalId
        {
            SerialNo = serialNo,
            Make = deviceInfo.Make,
            Model = deviceInfo.Model,
            Type = SbiMapping.MapBiometricType(deviceInfo.Modality),
            DeviceSubType = SbiMapping.MapDeviceSubType(deviceInfo.DeviceSubType),
            DeviceProvider = deviceInfo.DeviceProvider,
            DeviceProviderId = deviceInfo.DeviceProviderId,
            DateTime = Now(),
        };
    }

    /// <summary>
    /// 取当前时刻的报文写法
    /// </summary>
    /// <returns>
    /// ISO 8601 时刻
    /// </returns>
    /// <remarks>
    /// <b>秒级、UTC、带 Z、不带毫秒</b>：CTK 对 digitalId 的 <c>dateTime</c> 有格式要求（SBI1083），
    /// 带毫秒会与之不符。
    /// </remarks>
    private static string Now()
        => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
