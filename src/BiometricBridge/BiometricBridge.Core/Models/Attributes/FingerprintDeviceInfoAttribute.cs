using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Core.Models.Attributes;

/// <summary>
/// 指纹设备信息特性。
/// </summary>
public class FingerprintDeviceInfoAttribute : BiometricDeviceInfoAttribute
{
    /// <inheritdoc/>
    public override BiometricType Modality => BiometricType.Finger;
}
