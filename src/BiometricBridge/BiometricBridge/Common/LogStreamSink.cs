using System;
using Serilog.Core;
using Serilog.Events;

namespace BiometricBridge.Common;

/// <summary>
/// Serilog 到 <see cref="LogStreamService"/> 的适配器：把日志事件翻译成 <see cref="LogEntry"/> 后转交。
/// </summary>
/// <remarks>
/// <para>
/// 不持有任何状态：翻译、转发，仅此而已（缓冲与通知都在服务里）。
/// </para>
/// <para>
/// 刻意不做成配置型 sink（<c>serilog.yaml</c> 里 File 那样的 <c>Using</c> + <c>Name</c>）：它要写入的是应用自己的
/// 服务，而 Serilog.Settings.Configuration 只能从配置项里取构造参数，注入不了容器里的实例；硬要那样接，sink 就只能
/// 靠静态全局去够那个实例，且"名字绑不上"是静默的、没有编译期检查。故与其它 sink 一起在 <c>App.ConfigLog</c> 里装配。
/// </para>
/// </remarks>
public sealed class LogStreamSink : ILogEventSink
{
    /// <summary>
    /// 日志行落地的服务，与界面看到的是同一个实例。
    /// </summary>
    private readonly LogStreamService _stream;

    /// <summary>
    /// 构造适配器。
    /// </summary>
    /// <param name="stream">
    /// 日志流服务。
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="stream"/> 为 null。
    /// </exception>
    public LogStreamSink(LogStreamService stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
    }

    /// <inheritdoc/>
    /// <param name="logEvent">
    /// Serilog 日志事件。
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="logEvent"/> 为 null。
    /// </exception>
    public void Emit(LogEvent logEvent)
    {
        ArgumentNullException.ThrowIfNull(logEvent);

        _stream.Publish(new LogEntry(
            logEvent.Timestamp,
            logEvent.Level,
            logEvent.RenderMessage(),
            logEvent.Exception?.ToString()));
    }
}
