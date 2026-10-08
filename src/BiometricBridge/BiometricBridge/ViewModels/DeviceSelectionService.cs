using CommunityToolkit.Mvvm.ComponentModel;

namespace BiometricBridge.ViewModels;

/// <summary>
/// 设备列表与设备控制面板共享的"当前选中设备"。
/// </summary>
/// <remarks>
/// 单例。<see cref="DeviceListViewModel"/> 写入（用户在列表里选择），
/// <see cref="DeviceControlViewModel"/> 读取（控制面板只作用于选中的那台）。
/// 两个 VM 拿到的是同一个 <see cref="DeviceInfoItemViewModel"/> 实例，因此列表项被刷新时，
/// 控制面板看到的也是同一份最新状态 —— 无需在两个 VM 之间复制字段。
/// </remarks>
public partial class DeviceSelectionService : ObservableObject
{
    /// <summary>
    /// 当前选中的设备；null 表示未选中任何设备。
    /// </summary>
    [ObservableProperty] private DeviceInfoItemViewModel? _selectedDeviceItem;
}
