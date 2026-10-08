using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Core.Models;

public class FingerprintDeviceInfo:DeviceInfo
{
    public override BiometricModality Modality => BiometricModality.Finger;
}