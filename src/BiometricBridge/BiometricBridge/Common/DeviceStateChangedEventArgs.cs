using System;
using BiometricBridge.Core.Models;

namespace BiometricBridge.Common;

/// <summary>
/// <see cref="BiometricDeviceManager.DeviceStateChanged"/> 的事件参数。
/// </summary>
public sealed class DeviceStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 状态变化后的设备快照。
    /// </summary>
    /// <remarks>
    /// 带上完整快照，订阅方无需回头查询管理器。
    /// </remarks>
    public required DeviceSnapshot Snapshot { get; init; }
}
