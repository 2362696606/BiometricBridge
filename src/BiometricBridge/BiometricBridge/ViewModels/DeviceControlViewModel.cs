using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Threading;
using BiometricBridge.Common;
using BiometricBridge.Core.Models.Enums;
using BiometricBridge.Host;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BiometricBridge.ViewModels;

/// <summary>
/// 设备控制视图模型：对列表中选中的设备执行连接/断开、更换证书，并手动设置其状态。
/// </summary>
/// <remarks>
/// 与列表共享同一台设备：选中项来自 <see cref="DeviceSelectionService"/>，与
/// <see cref="DeviceListViewModel"/> 拿到的是同一个 <see cref="DeviceInfoItemViewModel"/> 实例。
/// 本 VM 不写共享选中项 —— 控制面板只读选择，无权改变它。
/// </remarks>
public partial class DeviceControlViewModel : ObservableObject
{
    #region Fileds

    private readonly BiometricDeviceManager _deviceManager;

    private readonly DeviceSelectionService _selection;

    private readonly DeviceCertificateService _certificates;

    #endregion

    /// <summary>
    /// 构造视图模型。
    /// </summary>
    /// <param name="deviceManager">
    /// 设备管理器。
    /// </param>
    /// <param name="selection">
    /// 共享的设备选中服务。
    /// </param>
    /// <param name="certificates">
    /// 设备证书服务，换证书经它。
    /// </param>
    /// <remarks>
    /// 订阅而不退订：本 VM 与管理器、选中服务同为进程级寿命，一起随进程结束，不构成泄漏。
    /// </remarks>
    public DeviceControlViewModel(
        BiometricDeviceManager deviceManager,
        DeviceSelectionService selection,
        DeviceCertificateService certificates)
    {
        _deviceManager = deviceManager;
        _selection = selection;
        _certificates = certificates;

        _deviceManager.DeviceStateChanged += OnDeviceStateChanged;
        _selection.PropertyChanged += OnSelectionChanged;

        SeedFromSelection();
    }

    #region ObservableProperties

    /// <summary>
    /// 选中设备是否已连接。
    /// </summary>
    /// <remarks>
    /// 自持一份，而非读列表项：管理器事件与列表刷新是两条独立的 Post，列表项可能尚未刷新，
    /// 直接读它会拿到旧值。真正的权威来源是事件里的快照（见 <see cref="OnDeviceStateChanged"/>）。
    /// </remarks>
    [ObservableProperty] private bool _isConnected;

    /// <summary>
    /// 选中设备的状态，绑定到下拉框。null 表示未选中任何设备。
    /// </summary>
    [ObservableProperty] private BiometricDeviceStatus? _deviceStatus;

    #endregion

    #region Properties

    /// <summary>
    /// 当前选中的设备；只读。控制面板无权改变选择，故这里只做代理。
    /// </summary>
    public DeviceInfoItemViewModel? SelectedDeviceItem => _selection.SelectedDeviceItem;

    /// <summary>
    /// 是否已选中设备。无选中时禁用状态下拉框。
    /// </summary>
    public bool HasSelection => SelectedDeviceItem is not null;

    #endregion

    #region Commands

    /// <summary>
    /// 连接选中设备。
    /// </summary>
    /// <returns>
    /// 表示异步连接操作的任务。
    /// </returns>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        if (SelectedDeviceItem is not { } item)
        {
            return;
        }

        try
        {
            await _deviceManager.ConnectAsync(item.DeviceId);
        }
        catch (Exception)
        {
            // 设备层的失败（含异常与耗时）已由 LoggingDeviceDecorator 记录，此处不重复记，
            // 只负责不让 async 命令的异常无人观测。将来要把失败显示到界面，从这里接。
        }
    }

    /// <summary>
    /// 连接命令可否执行：已选中且当前未连接。
    /// </summary>
    /// <returns>
    /// 可执行返回 true。
    /// </returns>
    private bool CanConnect()
    {
        return SelectedDeviceItem is not null && !IsConnected;
    }

    /// <summary>
    /// 断开选中设备。
    /// </summary>
    /// <returns>
    /// 表示异步断开操作的任务。
    /// </returns>
    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private async Task DisconnectAsync()
    {
        if (SelectedDeviceItem is not { } item)
        {
            return;
        }

        try
        {
            await _deviceManager.DisconnectAsync(item.DeviceId);
        }
        catch (Exception)
        {
            // 同 ConnectAsync：留痕由 LoggingDeviceDecorator 负责，此处只防异常无人观测。
        }
    }

    /// <summary>
    /// 断开命令可否执行：已选中且当前已连接。
    /// </summary>
    /// <returns>
    /// 可执行返回 true。
    /// </returns>
    private bool CanDisconnect()
    {
        return SelectedDeviceItem is not null && IsConnected;
    }

    /// <summary>
    /// 更换选中设备的证书。
    /// </summary>
    /// <remarks>
    /// 同步命令：签发只用内存里的设备密钥与 DP 材料，不碰设备硬件，耗时可以忽略。
    /// </remarks>
    [RelayCommand(CanExecute = nameof(CanRotateCertificate))]
    private void RotateCertificate()
    {
        if (SelectedDeviceItem is not { } item)
        {
            return;
        }

        try
        {
            _certificates.Rotate(item.DeviceId);
            Log.Information("已为设备 {SerialNo} 更换证书。", item.SerialNo);
        }
        catch (Exception exception)
        {
            // 换证书失败没有别的界面出口，故就地记一条：日志视图是操作员唯一能看到结果的地方。
            Log.Error(exception, "更换设备证书失败。");
        }
    }

    /// <summary>
    /// 换证书命令可否执行：设备得连着 —— 设备证书的 Subject 里要写序列号，而序列号只有连上才报得出来。
    /// </summary>
    /// <returns>
    /// 可执行返回 true。
    /// </returns>
    private bool CanRotateCertificate()
    {
        return SelectedDeviceItem is { IsConnected: true, SerialNo.Length: > 0 };
    }

    #endregion

    /// <summary>
    /// 状态下拉框被改写：把新状态写回管理器。
    /// </summary>
    /// <param name="value">
    /// 新状态。
    /// </param>
    /// <remarks>
    /// 切换设备时也会走这里（把下拉框回填成新设备的当前状态），此时写入的值与设备当前状态相同，
    /// 管理器的幂等会让它退化为空操作，故无需额外的"抑制回写"标志。
    /// </remarks>
    partial void OnDeviceStatusChanged(BiometricDeviceStatus? value)
    {
        if (value is not { } status || SelectedDeviceItem is not { } item)
        {
            return;
        }

        try
        {
            _deviceManager.SetDeviceStatus(item.DeviceId, status);
        }
        catch (Exception)
        {
            // 改的是管理器持有的状态，不经设备、也走不到 LoggingDeviceDecorator；
            // 唯一的失败是 id 不存在（id 取自管理器快照，实际不会发生），故只兜住不记。
        }
    }

    /// <summary>
    /// 共享选中项变化：重新对齐到新设备。
    /// </summary>
    /// <param name="sender">
    /// 事件源。
    /// </param>
    /// <param name="e">
    /// 事件参数。
    /// </param>
    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DeviceSelectionService.SelectedDeviceItem))
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedDeviceItem));
        SeedFromSelection();
    }

    /// <summary>
    /// 设备状态变化：只关心当前选中的那台。
    /// </summary>
    /// <param name="sender">
    /// 事件源。
    /// </param>
    /// <param name="e">
    /// 事件参数。
    /// </param>
    /// <remarks>
    /// 事件在设备调用完成的线程上触发（通常是线程池线程），而绑定属性只能在 UI 线程改，故切回 UI 线程。
    /// </remarks>
    private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        // 只捕获不可变快照，不捕获事件参数对象：Post 会把这个闭包留到调度器执行它为止。
        var snapshot = e.Snapshot;

        // 用 Post 而非 Invoke：设备调用线程不该同步等 UI 线程。
        Dispatcher.UIThread.Post(() =>
        {
            // 只处理选中设备：否则连接设备 B 会把正显示设备 A 的面板改掉。
            if (snapshot.DeviceId != SelectedDeviceItem?.DeviceId)
            {
                return;
            }

            IsConnected = snapshot.IsConnected;
            DeviceStatus = snapshot.DeviceStatus;
            RefreshCommands();
        });
    }

    /// <summary>
    /// 把面板对齐到当前选中设备的初始状态。
    /// </summary>
    /// <remarks>
    /// 回填 <see cref="DeviceStatus"/> 会触发写回，但写入的正是该设备的当前状态，
    /// 被管理器的幂等吃掉，不会产生状态迁移。
    /// </remarks>
    private void SeedFromSelection()
    {
        if (SelectedDeviceItem is { } item)
        {
            IsConnected = item.IsConnected;
            DeviceStatus = item.Status;
        }
        else
        {
            IsConnected = false;
            DeviceStatus = null;
        }

        OnPropertyChanged(nameof(HasSelection));
        RefreshCommands();
    }

    /// <summary>
    /// 重新求值连接/断开命令的可执行性。
    /// </summary>
    private void RefreshCommands()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        DisconnectCommand.NotifyCanExecuteChanged();
        RotateCertificateCommand.NotifyCanExecuteChanged();
    }
}
