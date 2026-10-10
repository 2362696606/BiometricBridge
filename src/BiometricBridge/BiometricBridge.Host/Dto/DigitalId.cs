using BiometricBridge.Host.Dto.Enum;

namespace BiometricBridge.Host.Dto;

/// <summary>
/// 数字标识：设备的身份，由设备供应方在出厂时确定。
/// </summary>
/// <remarks>
/// <para>
/// 同一个结构用在两处，区别只在签不签名：设备发现响应里的那个是<b>未签名</b>的，
/// 采集响应载荷里的那个随整个载荷一起被<b>签名</b>。
/// </para>
/// <para>
/// 各项都是设备的物理事实，取自设备自身，本服务不推断。
/// </para>
/// </remarks>
public sealed record DigitalId
{
    /// <summary>序列号，与设备物理标识上印的一致。</summary>
    public required string SerialNo { get; init; }

    /// <summary>厂商，与设备物理标识上印的一致。</summary>
    public required string Make { get; init; }

    /// <summary>型号，与设备物理标识上印的一致。</summary>
    public required string Model { get; init; }

    /// <summary>生物特征模态。</summary>
    public required BiometricType Type { get; init; }

    /// <summary>设备子类型。</summary>
    public required DeviceSubType DeviceSubType { get; init; }

    /// <summary>设备供应方名称。</summary>
    public required string DeviceProvider { get; init; }

    /// <summary>设备供应方标识。</summary>
    public required string DeviceProviderId { get; init; }

    /// <summary>本次请求的签发时刻。</summary>
    public required string DateTime { get; init; }
}
