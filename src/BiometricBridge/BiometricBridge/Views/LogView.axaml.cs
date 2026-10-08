using System;
using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using BiometricBridge.ViewModels;

namespace BiometricBridge.Views;

public partial class LogView : UserControl
{
    private INotifyCollectionChanged? _observed;
    private readonly ListBox? _list;

    public LogView()
    {
        InitializeComponent();

        // 按名字在运行时取，而非依赖 XAML 编译器生成的字段：XAML 一旦没给这个名字，
        // 字段就不存在，编译期直接报 CS0103；运行时查找则退化为"不自动滚动"，不炸编译。
        _list = this.FindControl<ListBox>("LogList");
    }

    /// <summary>
    /// 数据上下文到位（Prism 自动装配 VM）后，盯住日志集合，以便新行出现时滚到底。
    /// </summary>
    /// <param name="e">
    /// 事件参数。
    /// </param>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // 先退订旧的：DataContext 可能被换多次，否则会重复订阅。
        if (_observed is not null)
        {
            _observed.CollectionChanged -= OnLogsChanged;
        }

        _observed = (DataContext as LogViewModel)?.Logs;
        if (_observed is not null)
        {
            _observed.CollectionChanged += OnLogsChanged;
        }
    }

    /// <summary>
    /// 离开可视树时退订。
    /// </summary>
    /// <param name="e">
    /// 事件参数。
    /// </param>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);

        if (_observed is not null)
        {
            _observed.CollectionChanged -= OnLogsChanged;
            _observed = null;
        }
    }

    /// <summary>
    /// 新增一行时把它滚入视野：日志的注意力总在最新一条上。
    /// </summary>
    /// <param name="sender">
    /// 事件源。
    /// </param>
    /// <param name="e">
    /// 事件参数。
    /// </param>
    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 只在追加时滚动：裁剪旧行会触发 Remove，不该把视图拽回去。
        if (_list is null || e.Action != NotifyCollectionChangedAction.Add || _list.ItemCount == 0)
        {
            return;
        }

        _list.ScrollIntoView(_list.ItemCount - 1);
    }
}
