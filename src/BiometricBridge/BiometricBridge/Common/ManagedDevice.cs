using System.Security.Cryptography;
using BiometricBridge.Core;
using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Common;

public class ManagedDevice
{
    /// <summary>
    /// 设备对象
    /// </summary>
    public required IBiometricDevice Device { get; set; }

    /// <summary>
    /// 设备状态，手动向外提供
    /// </summary>
    public BiometricDeviceStatus DeviceStatus { get; set; }
}