using System;
using System.Threading;
using System.Threading.Tasks;
using BiometricBridge.Core;

namespace BiometricBridge.Common;

/// <summary>
/// 一次预览会话：取消源、结束任务与"是否被主动停止"绑在一起。
/// </summary>
/// <remarks>
/// 仅供 <see cref="BiometricDeviceManager"/> 使用。做成一个类型而不是在设备条目上散成三个字段，
/// 是因为这三者必须一起判读（例如"停止时按会话比对"要同时用到取消源与结束任务）。
/// </remarks>
internal sealed class PreviewSession
{
    /// <summary>
    /// 构造会话。
    /// </summary>
    /// <param name="sink">
    /// 帧接收端。
    /// </param>
    internal PreviewSession(PreviewFrameSink sink)
    {
        Sink = sink;
    }

    /// <summary>
    /// 帧接收端。
    /// </summary>
    internal PreviewFrameSink Sink { get; }

    /// <summary>
    /// 取消源。取消即请求设备停流；由 <see cref="BiometricDeviceManager"/> 释放。
    /// </summary>
    internal CancellationTokenSource Cancellation { get; } = new();

    /// <summary>
    /// 会话的结束任务。完成即表示设备侧的流已结束——因此串行化门也已释放，设备可以接别的操作了。
    /// </summary>
    internal Task Completion { get; set; } = Task.CompletedTask;

    /// <summary>
    /// 会话结束时的异常；null 表示正常结束。
    /// </summary>
    /// <remarks>
    /// 收在这里而不是让 <see cref="Completion"/> 直接带故障：自行结束的会话没人 await 它，
    /// 故障任务会变成"未观测异常"。停止方需要知道失败时，由管理器把它重抛出去。
    /// </remarks>
    internal Exception? Failure { get; set; }

    /// <summary>
    /// 是否是调用方主动停的。
    /// </summary>
    /// <remarks>
    /// 只有"自己停下来的"才广播 <see cref="BiometricDeviceManager.PreviewStateChanged"/>：
    /// 主动停的那次调用方本来就知道；而广播会与新起的会话打架——先停 A 再起 B 时，
    /// A 的停止通知可能晚于 B 的开始通知，把 B 的状态冲掉。
    /// </remarks>
    internal bool StopRequested { get; set; }
}
