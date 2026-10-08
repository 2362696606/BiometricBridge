using System;
using Serilog.Events;

namespace BiometricBridge.Common;

/// <summary>
/// 一条日志行的不可变快照，供界面显示。
/// </summary>
/// <param name="Timestamp">
/// 记录时间。
/// </param>
/// <param name="Level">
/// 日志级别。
/// </param>
/// <param name="Message">
/// 已按消息模板渲染好的文本。
/// </param>
/// <param name="Exception">
/// 异常文本；无异常时为 <c>null</c>。
/// </param>
/// <remarks>
/// 纯数据、无通知：新日志的通知在 <see cref="LogStreamService"/> 上，不在本类型上。
/// </remarks>
public sealed record LogEntry(
    DateTimeOffset Timestamp,
    LogEventLevel Level,
    string Message,
    string? Exception);
