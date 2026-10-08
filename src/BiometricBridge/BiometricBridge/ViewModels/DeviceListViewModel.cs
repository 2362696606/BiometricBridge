using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using BiometricBridge.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiometricBridge.ViewModels;

public partial class DeviceListViewModel : ObservableObject
{
    #region Fileds

    private readonly BiometricDeviceManager _deviceManager;

    private readonly DeviceSelectionService _selection;

    #endregion

    public DeviceListViewModel(BiometricDeviceManager deviceManager, DeviceSelectionService selection)
    {
        _deviceManager = deviceManager;
        _selection = selection;

        foreach (var snapshot in deviceManager.GetDeviceSnapshots())
        {
            var item = CreateItem(snapshot);
            if (item is not null)
            {
                Devices.Add(item);
            }
        }

        // 订阅而不退订：本 VM 与管理器同为进程级寿命（shell 只构建一次，管理器是单例），
        // 两者一起随进程结束，不构成泄漏。若将来 MainView 变得可重建（导航/区域/多窗口），
        // 正确的修法不是在 VM 上实现 IDisposable（Prism 不缓存 VM 实例、容器也不会被释放，
        // 那个 Dispose 永远不会被调到），而是在视图的 DetachedFromVisualTree 里退订。
        _deviceManager.DeviceStateChanged += OnDeviceStateChanged;

        // 选中项的写入端在本列表（用户在列表里选择），服务再把它交给控制面板。
        _selection.PropertyChanged += OnSelectionChanged;
    }

    #region ObservableProperties

    /// <summary>
    /// 设备列表
    /// </summary>
    [ObservableProperty] private ObservableCollection<DeviceInfoItemViewModel> _devices = [];

    /// <summary>
    /// 选中的设备。读写都转发到共享的 <see cref="DeviceSelectionService"/>，
    /// 使本列表与设备控制面板作用于同一台设备（同一个列表项实例）。
    /// </summary>
    public DeviceInfoItemViewModel? SelectedDeviceItem
    {
        get => _selection.SelectedDeviceItem;
        set => _selection.SelectedDeviceItem = value;
    }

    #endregion

    /// <summary>
    /// 共享选中项被改写：重发本属性，让列表的 SelectedItem 跟随。
    /// </summary>
    /// <param name="sender">
    /// 事件源。
    /// </param>
    /// <param name="e">
    /// 事件参数。
    /// </param>
    /// <remarks>
    /// 用户在本列表选择时，setter 已写入服务，服务再回抛本事件；此处只是让绑定重读 getter，
    /// 值相同不会造成循环。
    /// </remarks>
    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DeviceSelectionService.SelectedDeviceItem))
        {
            OnPropertyChanged(nameof(SelectedDeviceItem));
        }
    }

    /// <summary>
    /// 设备状态变化：更新对应列表项。
    /// </summary>
    /// <param name="sender">
    /// 事件源。
    /// </param>
    /// <param name="e">
    /// 事件参数。
    /// </param>
    /// <remarks>
    /// 事件在设备调用完成的线程上触发（设备层与管理器一律 <c>ConfigureAwait(false)</c>，通常是线程池
    /// 线程），而绑定属性只能在 UI 线程改，故切回 UI 线程。
    /// </remarks>
    private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        // 只捕获不可变快照，不捕获事件参数对象：Post 会把这个闭包留到调度器执行它为止。
        var snapshot = e.Snapshot;

        // 用 Post 而非 Invoke：后者会让设备调用所在的线程同步等 UI 线程，形成反向依赖。
        // 代价是更新晚一个调度轮次，故"ConnectAsync 返回"不等于"列表已刷新"；Post 之间保持
        // FIFO，多次变更不会乱序。
        Dispatcher.UIThread.Post(() => Update(snapshot));
    }

    /// <summary>
    /// 把快照写回列表项。
    /// </summary>
    /// <param name="snapshot">
    /// 设备快照。
    /// </param>
    private void Update(DeviceSnapshot snapshot)
    {
        // 构造时因缺静态信息被跳过（或列表里本就没有该设备），无需更新。
        if (snapshot.Info is null)
        {
            return;
        }

        var item = Devices.FirstOrDefault(device => device.DeviceId == snapshot.DeviceId);
        if (item is null)
        {
            return;
        }

        item.SerialNo = snapshot.Info.SerialNo;
        item.Status = snapshot.DeviceStatus;
        item.IsConnected = snapshot.IsConnected;
    }

    /// <summary>
    /// 由快照构造列表项。
    /// </summary>
    /// <param name="snapshot">
    /// 设备快照。
    /// </param>
    /// <returns>
    /// 列表项；设备类漏标 <c>BiometricDeviceInfoAttribute</c> 时拿不到静态信息，返回 null 不显示。
    /// </returns>
    private static DeviceInfoItemViewModel? CreateItem(DeviceSnapshot snapshot)
    {
        var deviceInfo = snapshot.Info;
        if (deviceInfo is null)
        {
            return null;
        }

        return new DeviceInfoItemViewModel
        {
            DeviceId = snapshot.DeviceId,
            Make = deviceInfo.Make,
            Model = deviceInfo.Model,
            DeviceProvider = deviceInfo.DeviceProvider,
            DeviceProviderId = deviceInfo.DeviceProviderId,
            DeviceSubIds = deviceInfo.DeviceSubIds,
            DeviceSubType = deviceInfo.DeviceSubType,
            Certification = deviceInfo.Certification,
            Purpose = deviceInfo.Purpose,
            Modality = deviceInfo.Modality,
            SerialNo = deviceInfo.SerialNo,
            Status = snapshot.DeviceStatus,
            IsConnected = snapshot.IsConnected,
        };
    }
}
