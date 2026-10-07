namespace BiometricBridge.Core.Models.Enums;

/// <summary>
/// 生物识别设备设备状态
/// </summary>
public enum BiometricDeviceStatus
{
    /// <summary>
    /// 就绪
    /// </summary>
    Ready,
    
    /// <summary>
    /// 忙
    /// </summary>
    Busy,
    
    /// <summary>
    /// 未就绪
    /// </summary>
    NotReady,
    
    /// <summary>
    /// 未注册
    /// </summary>
    NotRegistered
}