using System;
using BiometricBridge.Core.Models.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiometricBridge.ViewModels;

public partial class DeviceInfoItemViewModel : ObservableObject
{
    /// <summary>
    /// 设备id
    /// </summary>
    [ObservableProperty] private Guid _deviceId;

    /// <summary>
    /// 厂商。
    /// </summary>
    [ObservableProperty] private string _make = string.Empty;

    /// <summary>
    /// 型号。
    /// </summary>
    [ObservableProperty] private string _model = string.Empty;

    /// <summary>
    /// 设备供应商名称。
    /// </summary>
    [ObservableProperty] private string _deviceProvider = string.Empty;

    /// <summary>
    /// 设备供应商标识。
    /// </summary>
    [ObservableProperty] private string _deviceProviderId = string.Empty;

    /// <summary>
    /// 本设备支持的设备子 ID。
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(DeviceSubIdsText))]
    private int[] _deviceSubIds = [];

    /// <summary>
    /// 设备子 ID 的展示文本。数组无法直接绑定，故拼成逗号分隔的字符串。
    /// </summary>
    public string DeviceSubIdsText => string.Join(", ", DeviceSubIds);

    /// <summary>
    /// 本设备的子类型。
    /// </summary>
    [ObservableProperty] private DeviceSubType _deviceSubType;

    /// <summary>
    /// 本设备的认证等级。
    /// </summary>
    [ObservableProperty] private CertificationLevel _certification;

    /// <summary>
    /// 本设备的用途。
    /// </summary>
    [ObservableProperty] private Purpose _purpose;

    /// <summary>
    /// 本设备的生物特征模态。
    /// </summary>
    [ObservableProperty] private BiometricType _modality;

    /// <summary>
    /// 设备sn码
    /// </summary>
    [ObservableProperty] private string? _serialNo;

    /// <summary>
    /// 设备状态
    /// </summary>
    [ObservableProperty] private BiometricDeviceStatus _status;
}