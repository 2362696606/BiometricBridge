using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Serilog.Events;

namespace BiometricBridge.Converters;

/// <summary>
/// 判断日志级别是否属于 <c>ConverterParameter</c> 指定的类别，用于给级别文本挂上对应的样式类。
/// </summary>
/// <remarks>
/// 输出是 bool，绑定到 Avalonia 的 <c>Classes.xxx</c>：为 true 时挂上该类，样式据此上色。
/// 用转换器而不是在日志行类型上备一组 bool 属性：级别到"危险 / 警告"的映射是纯显示关切，
/// 不该混进 <see cref="BiometricBridge.Common.LogEntry"/>。
/// </remarks>
public sealed class LogLevelMatchConverter : IValueConverter
{
    /// <summary>
    /// 单例，供 XAML 以 <c>{x:Static}</c> 引用，省去在每个视图里声明资源。
    /// </summary>
    public static readonly LogLevelMatchConverter Instance = new();

    /// <inheritdoc/>
    /// <param name="value">
    /// 日志级别。
    /// </param>
    /// <param name="targetType">
    /// 目标类型（bool），未使用。
    /// </param>
    /// <param name="parameter">
    /// 类别名：<c>Error</c> 命中 Error 及以上，<c>Warning</c> 命中 Warning。
    /// </param>
    /// <param name="culture">
    /// 区域性，未使用。
    /// </param>
    /// <returns>
    /// 命中返回 true。
    /// </returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not LogEventLevel level || parameter is not string category)
        {
            return false;
        }

        return category switch
        {
            // Fatal 也归入"危险"：两者都该用告警色，故用 >= 而非 ==。
            "Error" => level >= LogEventLevel.Error,
            "Warning" => level == LogEventLevel.Warning,
            _ => false
        };
    }

    /// <inheritdoc/>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
