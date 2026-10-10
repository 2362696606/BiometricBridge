using System.Threading;
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
    /// 是否有采集正在进行（0/1）。
    /// </summary>
    /// <remarks>
    /// 用 <see cref="Interlocked"/> 的原子交换而不是普通布尔：并行到达的两次采集要"只有一个拿到"，
    /// 靠读改写会有竞态，两个都可能通过。取到"忙"的那次由管理器以 <see cref="DeviceBusyException"/> 拒绝。
    /// </remarks>
    private int _captureInFlight;

    /// <summary>
    /// 设备对象。
    /// </summary>
    internal required IBiometricDevice Device { get; init; }

    /// <summary>
    /// 设备状态。
    /// </summary>
    /// <remarks>
    /// 仅管理器可写：本类型是 internal，且唯一调用方就是 <see cref="BiometricDeviceManager"/>，
    /// 故 set 的可达范围仍被限制在管理器内。迁移来源有两处：管理器的
    /// <see cref="BiometricDeviceManager.SetDeviceStatus"/>（手动设置），以及
    /// <see cref="BiometricDeviceManager.CaptureAsync"/> 在采集期间临时置 <c>Busy</c>、结束后还原
    /// —— 后者是为让 <c>/info</c>、<c>/device</c> 在采集期间报出规范要求的"忙"。
    /// </remarks>
    internal BiometricDeviceStatus DeviceStatus { get; set; } = BiometricDeviceStatus.Ready;

    /// <summary>
    /// 保护 <see cref="Preview"/>。
    /// </summary>
    /// <remarks>
    /// 与设备访问（<c>SerializingDeviceDecorator</c> 的门）是两把不同的锁，不可混用：
    /// 停止预览时若去抢设备门，就会与"等它放开门"的预览任务互相吊死。
    /// 本锁只保护这一个字段，临界区里不做任何等待。
    /// </remarks>
    internal object PreviewGate { get; } = new();

    /// <summary>
    /// 当前预览会话；null 表示没有预览在跑。
    /// </summary>
    internal PreviewSession? Preview { get; set; }

    /// <summary>
    /// 标记一次采集开始。
    /// </summary>
    /// <returns>
    /// 拿到采集权返回 true；已有采集在进行返回 false
    /// </returns>
    internal bool TryBeginCapture() => Interlocked.CompareExchange(ref _captureInFlight, 1, 0) == 0;

    /// <summary>
    /// 标记一次采集结束（须与成功的 <see cref="TryBeginCapture"/> 配对调用）。
    /// </summary>
    internal void EndCapture() => Interlocked.Exchange(ref _captureInFlight, 0);
}
