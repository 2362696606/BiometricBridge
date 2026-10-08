using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Core.Models.Attributes;

/// <summary>
/// 生物识别设备信息特性
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public abstract class BiometricDeviceInfoAttribute : Attribute
{
    /// <summary>
    /// 厂商。
    /// </summary>
    public required string Make { get; init; }

    /// <summary>
    /// 型号。
    /// </summary>
    public required string Model { get; init; }

    /// <summary>
    /// 设备供应商名称。
    /// </summary>
    public required string DeviceProvider { get; init; }

    /// <summary>
    /// 设备供应商标识。
    /// </summary>
    public required string DeviceProviderId { get; init; }

    /// <summary>
    /// 本设备支持的设备子 ID。
    /// </summary>
    /// <remarks>
    /// 子 ID 是设备选择"用哪个子模块采集"的索引；单指设备通常只有 <c>0</c>，
    /// 四指联采设备为 <c>1</c>（左四指）、<c>2</c>（右四指）、<c>3</c>（双拇指）。
    /// </remarks>
    public required int[] DeviceSubIds { get; init; }

    /// <summary>
    /// 本设备的子类型。
    /// </summary>
    public required DeviceSubType DeviceSubType { get; init; }

    /// <summary>
    /// 本设备的认证等级。
    /// </summary>
    public required CertificationLevel Certification { get; init; }

    /// <summary>
    /// 本设备的用途。
    /// </summary>
    public required Purpose Purpose { get; init; }

    /// <summary>
    /// 本设备的生物特征模态。
    /// </summary>
    public abstract BiometricModality Modality { get; }
}