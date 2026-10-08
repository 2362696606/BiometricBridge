using System.Collections.ObjectModel;
using Avalonia.Threading;
using BiometricBridge.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BiometricBridge.ViewModels;

/// <summary>
/// 日志视图模型：把 <see cref="LogStreamService"/> 缓冲的运行期日志呈现到界面。
/// </summary>
/// <remarks>
/// 订阅而不退订：本 VM 与 <see cref="LogStreamService"/> 同为进程级寿命（服务是单例，shell 一次性构建 VM），
/// 一起随进程结束，不构成泄漏。若将来本视图可重建，退订应放在视图的 <c>DetachedFromVisualTree</c>
/// （Prism 不缓存 VM、容器也不会被释放，在 VM 上实现 IDisposable 那个 Dispose 永远不会被调到）——
/// 与 <see cref="DeviceListViewModel"/> 同一考量。
/// </remarks>
public partial class LogViewModel : ObservableObject
{
    private readonly LogStreamService _stream;

    /// <summary>
    /// 构造视图模型。
    /// </summary>
    /// <param name="stream">
    /// 进程内日志流，<see cref="LogStreamSink"/> 写的就是这个实例。
    /// </param>
    public LogViewModel(LogStreamService stream)
    {
        _stream = stream;

        // 缓冲先于界面存在（app 启动阶段就在写日志），故先把已有日志填进来，再接后续增量。
        foreach (var entry in stream.Snapshot())
        {
            Logs.Add(entry);
        }

        _stream.Emitted += OnLogEmitted;
    }

    #region ObservableProperties

    /// <summary>
    /// 日志行，按时间顺序追加。
    /// </summary>
    [ObservableProperty] private ObservableCollection<LogEntry> _logs = [];

    #endregion

    #region Commands

    /// <summary>
    /// 清空日志。
    /// </summary>
    /// <remarks>
    /// 连同底层缓冲一起清：否则缓冲里的旧日志会在下次填充时又被倒回来。
    /// </remarks>
    [RelayCommand]
    private void Clear()
    {
        _stream.Clear();
        Logs.Clear();
    }

    #endregion

    /// <summary>
    /// 新日志到达：切到 UI 线程追加。
    /// </summary>
    /// <param name="sender">
    /// 事件源。
    /// </param>
    /// <param name="entry">
    /// 日志行。
    /// </param>
    /// <remarks>
    /// 事件在写日志的线程上触发（Async sink 的消费线程或发起日志的线程），而绑定集合只能在 UI 线程改。
    /// 用 Post 而非 Invoke：写日志的线程不该同步等 UI 线程。Post 保持 FIFO，多条日志顺序不乱。
    /// </remarks>
    private void OnLogEmitted(object? sender, LogEntry entry)
    {
        Dispatcher.UIThread.Post(() => Append(entry));
    }

    /// <summary>
    /// 追加一行，并把界面侧的行数裁到与缓冲同一上限，避免长时间运行让集合无限增长。
    /// </summary>
    /// <param name="entry">
    /// 日志行。
    /// </param>
    private void Append(LogEntry entry)
    {
        while (Logs.Count >= LogStreamService.Capacity)
        {
            Logs.RemoveAt(0);
        }

        Logs.Add(entry);
    }
}
