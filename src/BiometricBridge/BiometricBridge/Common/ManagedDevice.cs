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
    /// 恒为 <see cref="BiometricDeviceStatus.Ready"/>：设备层没有忙/未就绪的语义，管理器只转发调用、
    /// 不制造状态迁移。用 init 而非 set，让这一点成为编译期约束。
    /// </remarks>
    internal BiometricDeviceStatus DeviceStatus { get; init; } = BiometricDeviceStatus.Ready;
}
