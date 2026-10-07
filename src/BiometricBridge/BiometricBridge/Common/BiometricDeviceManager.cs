using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Remote.Protocol.Input;
using BiometricBridge.Core;
using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Common;

/// <summary>
/// 设备管理器(应为单例)
/// </summary>
public class BiometricDeviceManager
{
    /// <summary>
    /// 设备字典
    /// </summary>
    private ConcurrentDictionary<Guid, ManagedDevice> _devices = new();

    public BiometricDeviceManager(IReadOnlyList<IBiometricDevice> devices)
    {
        foreach (var biometricDevice in devices)
        {
            var managedDevice = new ManagedDevice()
            {
                Device = biometricDevice,
                DeviceStatus = BiometricDeviceStatus.Ready,
            };
            var newGuid = Guid.NewGuid();
            _devices.TryAdd(newGuid, managedDevice);
        }
    }

    /// <summary>
    /// 设备字典
    /// </summary>
    public IReadOnlyDictionary<Guid, ManagedDevice> Devices => _devices;
}