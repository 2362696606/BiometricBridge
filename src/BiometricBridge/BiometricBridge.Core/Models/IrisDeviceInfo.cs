using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Core.Models;

public class IrisDeviceInfo : DeviceInfo
{
    public override BiometricModality Modality => BiometricModality.Iris;
}
