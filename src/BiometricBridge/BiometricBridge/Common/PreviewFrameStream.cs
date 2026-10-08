using System;
using System.Threading;
using Avalonia.Threading;
using BiometricBridge.Core;
using BiometricBridge.Core.Models;

namespace BiometricBridge.Common;

/// <summary>
/// 预览帧流：接住设备推来的帧，只留最新的一帧，并在界面线程上交出去。
/// </summary>
/// <remarks>
/// <para>
/// 它是设备世界与界面世界之间的那道口子。设备侧只认 <see cref="PreviewFrameSink"/>，
/// 界面侧只认 <see cref="FrameAvailable"/> —— 两边因此都不必知道对方的线程约定。
/// 界面那边的视图模型摸不到设备线程，设备那边的装饰链也摸不到界面。
/// </para>
/// <para>
/// 与 <see cref="LogStreamService"/> 同一分工：它自己也放 app 程序集而非 Core —— 服务于界面，
/// 是宿主的运行期状态，与设备、领域无关。
/// </para>
/// <para>
/// 只留最新一帧、不留历史：预览要的是"现在"，界面画得慢时丢的是旧帧，而不是让画面越积越滞后。
/// </para>
/// </remarks>
public sealed class PreviewFrameStream : IDisposable
{
    #region Fileds

    /// <summary>
    /// 最新一帧，等待界面线程取走。设备线程写、界面线程取，故经 <see cref="Interlocked"/> 传递。
    /// </summary>
    private PreviewFrame? _pending;

    /// <summary>
    /// 是否已有一次投递排在调度器里：1 表示有。
    /// </summary>
    /// <remarks>
    /// 它存在的意义就是让"已排队的投递"至多一次 —— 界面画得比设备推得慢时，队列深度由此恒为 1。
    /// </remarks>
    private int _deliveryScheduled;

    /// <summary>
    /// 是否在收帧。设备线程会读它，故标记 <see langword="volatile"/>。
    /// </summary>
    private volatile bool _accepting;

    #endregion

    /// <summary>
    /// 有一帧可用。
    /// </summary>
    /// <remarks>
    /// <b>在界面线程上触发</b>，订阅方可直接改绑定属性。这与 <see cref="LogStreamService.Emitted"/>
    /// 不同（那个在写入线程上触发、订阅方自行调度）—— 本类存在的意义正是把这一步做掉，
    /// 否则它和一条直通的事件没有区别。
    /// </remarks>
    public event EventHandler<PreviewFrame>? FrameAvailable;

    /// <summary>
    /// 开始收帧。
    /// </summary>
    /// <remarks>
    /// 起预览时调用。起点状态是不收帧：没有预览在跑时投来的帧属于上一场，留着只会演成"看着旧画面"。
    /// </remarks>
    public void Start()
    {
        _accepting = true;
    }

    /// <summary>
    /// 停止收帧并丢掉待投递的那一帧。
    /// </summary>
    /// <remarks>
    /// 停止预览时调用。<b>要立刻调用</b>，不能等设备侧停完：停止是"先落状态、再等设备侧收尾"，
    /// 等待期间在飞的帧若照收，刚清空的画面会被它们重新填回来。
    /// </remarks>
    public void Stop()
    {
        _accepting = false;

        // 已经排队的那次投递会取到 null，自然成为空操作。
        Interlocked.Exchange(ref _pending, null);
    }

    /// <summary>
    /// 收下一帧：留下最新的那帧，并保证至多有一次投递排在调度器里。
    /// </summary>
    /// <param name="frame">
    /// 预览帧。
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="frame"/> 为 null。
    /// </exception>
    /// <remarks>
    /// <b>由设备线程调用</b>：可能是厂商回调的栈帧，也可能是设备侧补帧定时器的线程池线程。
    /// 起预览时把它作为 <see cref="PreviewFrameSink"/> 交给设备。
    /// </remarks>
    public void OnFrame(PreviewFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!_accepting)
        {
            return;
        }

        Interlocked.Exchange(ref _pending, frame);

        // 已排队的投递会取走最新帧，无需再排。
        if (Interlocked.Exchange(ref _deliveryScheduled, 1) == 1)
        {
            return;
        }

        Dispatcher.UIThread.Post(PublishPending);
    }

    /// <summary>
    /// 把待投递的那一帧交给订阅者。跑在界面线程。
    /// </summary>
    /// <remarks>
    /// 先落调度标志再取帧：取帧之后新到的帧最多让本方法再被排一次，那一帧落进下一次调用 —— 一帧都不会丢。
    /// </remarks>
    private void PublishPending()
    {
        Volatile.Write(ref _deliveryScheduled, 0);

        if (Interlocked.Exchange(ref _pending, null) is not { } frame)
        {
            return;
        }

        // 排上队之后、执行之前可能已经被叫停，那时这一帧同样不该再露头。
        if (!_accepting)
        {
            return;
        }

        FrameAvailable?.Invoke(this, frame);
    }

    /// <summary>
    /// 停止收帧并释放。可重复调用。
    /// </summary>
    /// <remarks>
    /// 供退出路径使用：进程收尾时界面已拆到一半，此时再有帧进来只会往正在拆的东西上抛事件。
    /// 语义与 <see cref="Stop"/> 相同，只是读作"到此为止"。
    /// </remarks>
    public void Dispose()
    {
        Stop();
    }
}
