using System;
using System.Collections.Generic;

namespace BiometricBridge.Common;

/// <summary>
/// 进程内日志流：留住运行期日志，并在新日志到达时通知订阅者。
/// </summary>
/// <remarks>
/// <para>
/// 两类使用者各取所需：界面订阅 <see cref="Emitted"/> 看增量，构造时用 <see cref="Snapshot"/> 取存量。
/// 写入端是 <see cref="LogStreamSink"/> —— 它把 Serilog 事件翻译成 <see cref="LogEntry"/> 后交给
/// <see cref="Publish"/>，本类因此不认识任何日志实现类型。
/// </para>
/// <para>
/// 缓冲有上限，超出后丢弃最旧的一行，避免长时间运行把内存吃满。
/// </para>
/// <para>
/// 放在 app 程序集而非 Core：缓冲与通知服务于界面，是宿主的运行期状态，与设备、领域无关；
/// Core 是不带包依赖的抽象层。
/// </para>
/// </remarks>
public sealed class LogStreamService
{
    /// <summary>
    /// 缓冲上限（行）。超出后丢弃最旧的。界面侧同样按此上限裁剪，两边保持一致。
    /// </summary>
    public const int Capacity = 2000;

    private readonly Queue<LogEntry> _entries = new();
    private readonly object _gate = new();

    /// <summary>
    /// 新日志到达。
    /// </summary>
    /// <remarks>
    /// 在调用 <see cref="Publish"/> 的线程上触发 —— 写入端是 Serilog，调用线程是任意的
    /// （Async sink 的消费线程或发起日志的线程），订阅方需自行切到自己的线程（界面用 <c>Dispatcher.UIThread.Post</c>）。
    /// </remarks>
    public event EventHandler<LogEntry>? Emitted;

    /// <summary>
    /// 收下一行日志：入缓冲并通知订阅者。
    /// </summary>
    /// <param name="entry">
    /// 日志行。
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="entry"/> 为 null。
    /// </exception>
    public void Publish(LogEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        lock (_gate)
        {
            _entries.Enqueue(entry);

            while (_entries.Count > Capacity)
            {
                _entries.Dequeue();
            }
        }

        // 在锁外触发：订阅方会做 UI 调度，不该持着本锁。
        Emitted?.Invoke(this, entry);
    }

    /// <summary>
    /// 取当前缓冲的快照。
    /// </summary>
    /// <returns>
    /// 按时间顺序排列的日志行。
    /// </returns>
    /// <remarks>
    /// 给界面初次填充用：写入先于界面存在，构造时缓冲里可能已经有日志。
    /// </remarks>
    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToArray();
        }
    }

    /// <summary>
    /// 清空缓冲。
    /// </summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }
}
