using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Core.Models;

public class IrisDeviceInfo : DeviceInfo
{
    public override BiometricType Modality => BiometricType.Iris;
}
