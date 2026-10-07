using System.Reflection;
using BiometricBridge.Core.Models;
using BiometricBridge.Core.Models.Attributes;

namespace BiometricBridge.Core.Extensions;

public static class BiometricDeviceExtensions
{
    /// <summary>
    /// 读取设备的静态信息。
    /// </summary>
    /// <param name="device">
    /// 设备。可以是装饰器，装饰器对设备信息透明。
    /// </param>
    /// <returns>
    /// 设备信息；真实设备类上标了可识别的 <see cref="BiometricDeviceInfoAttribute"/> 时返回，否则返回 null。
    /// </returns>
    public static DeviceInfo? GetDeviceInfo(this IBiometricDevice device)
    {
        // 特性标在真实设备类上，而 device.GetType() 遇到装饰器只会返回装饰器类型，
        // 故反射前先剥到最内层。
        var innerDevice = device;
        while (innerDevice is IBiometricDeviceDecorator decorator)
        {
            innerDevice = decorator.InnerDevice;
        }

        var type = innerDevice.GetType();
        var biometricDeviceInfoAttribute = type.GetCustomAttribute<BiometricDeviceInfoAttribute>();
        if (biometricDeviceInfoAttribute is FingerprintDeviceInfoAttribute fingerprintDeviceInfoAttribute)
        {
            return new FingerprintDeviceInfo()
            {
                Make = fingerprintDeviceInfoAttribute.Make,
                Model = fingerprintDeviceInfoAttribute.Model,
                DeviceProvider = fingerprintDeviceInfoAttribute.DeviceProvider,
                DeviceProviderId = fingerprintDeviceInfoAttribute.DeviceProviderId,
                DeviceSubIds = [.. fingerprintDeviceInfoAttribute.DeviceSubIds],
                DeviceSubType = fingerprintDeviceInfoAttribute.DeviceSubType,
                Certification = fingerprintDeviceInfoAttribute.Certification,
                Purpose = fingerprintDeviceInfoAttribute.Purpose,
                SerialNo = innerDevice.SerialNo,
            };
        }

        return null;
    }
}