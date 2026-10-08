using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Core.Models.Attributes;

/// <summary>
/// 虹膜设备信息特性。
/// </summary>
public class IrisDeviceInfoAttribute : BiometricDeviceInfoAttribute
{
    /// <inheritdoc/>
    public override BiometricType Modality => BiometricType.Iris;
}
