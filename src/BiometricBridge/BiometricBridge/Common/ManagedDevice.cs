using BiometricBridge.Core;
using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Common;

/// <summary>
/// 管理器持有的设备条目：设备本体及其对外状态。
/// </summary>
/// <remarks>
/// 仅供 <see cref="BiometricDeviceManager"/> 使用。设备不允许流出管理器，故本类型对程序集外不可见，
/// 外部只能拿到 <see cref="DeviceSnapshot"/>。
/// </remarks>
internal sealed class ManagedDevice
{
    /// <summary>
    /// 设备对象。
    /// </summary>
    internal required IBiometricDevice Device { get; init; }

    /// <summary>
    /// 设备状态。
    /// </summary>
    /// <remarks>
    /// 仅管理器可写：本类型是 internal，且唯一调用方就是 <see cref="BiometricDeviceManager"/>，
    /// 故 set 的可达范围仍被限制在管理器内。状态的唯一迁移来源是管理器的
    /// <see cref="BiometricDeviceManager.SetDeviceStatus"/>（手动设置）—— 设备层没有忙/未就绪的语义，
    /// 管理器只转发调用，不自行制造迁移。
    /// </remarks>
    internal BiometricDeviceStatus DeviceStatus { get; set; } = BiometricDeviceStatus.Ready;
}
