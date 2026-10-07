namespace BiometricBridge.Core;

/// <summary>
/// 生物识别设备装饰器
/// </summary>
public interface IBiometricDeviceDecorator:IBiometricDevice
{
    public IBiometricDevice InnerDevice { get; }
}