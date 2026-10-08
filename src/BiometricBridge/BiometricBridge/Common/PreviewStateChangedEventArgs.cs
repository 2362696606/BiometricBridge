using System;

namespace BiometricBridge.Common;

/// <summary>
/// 预览启停事件的参数。
/// </summary>
/// <remarks>
/// 与"开始"和"停止"各来一个事件相比，合成一个带状态的参数既少一个类型，也让订阅方不可能搞错
/// 收到的顺序：状态是值，后一条总是覆盖前一条。
/// </remarks>
public sealed class PreviewStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 设备 id。
    /// </summary>
    public required Guid DeviceId { get; init; }

    /// <summary>
    /// 变更后是否正在预览。
    /// </summary>
    public required bool IsPreviewing { get; init; }
}
