using System;
using System.Collections.ObjectModel;
using BiometricBridge.Common;
using BiometricBridge.Core.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiometricBridge.ViewModels;

public partial class DeviceListViewModel : ObservableObject
{
    #region Fileds

    private readonly BiometricDeviceManager _deviceManager;

    #endregion

    public DeviceListViewModel(BiometricDeviceManager deviceManager)
    {
        _deviceManager = deviceManager;

        foreach (var (deviceId, managedDevice) in deviceManager.Devices)
        {
            // 设备类漏标 BiometricDeviceInfoAttribute 时拿不到静态信息，跳过不显示。
            var deviceInfo = managedDevice.Device.GetDeviceInfo();
            if (deviceInfo is null)
            {
                continue;
            }

            Devices.Add(new DeviceInfoItemViewModel
            {
                DeviceId = deviceId,
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
                Status = managedDevice.DeviceStatus,
            });
        }
    }

    #region ObservableProperties

    /// <summary>
    /// 设备列表
    /// </summary>
    [ObservableProperty] private ObservableCollection<DeviceInfoItemViewModel> _devices = [];

    /// <summary>
    /// 选中的设备id
    /// </summary>
    [ObservableProperty] private DeviceInfoItemViewModel? _selectedDeviceItem;

    #endregion
}