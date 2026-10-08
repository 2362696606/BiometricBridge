using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using BiometricBridge.Common;
using Prism.Ioc;

namespace BiometricBridge.Android;

[Activity(
    Label = "BiometricBridge.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    /// <summary>
    /// 真正退出时释放设备。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 只能挂在这里：<see cref="Avalonia.Controls.ApplicationLifetimes.IActivityApplicationLifetime"/>
    /// 只声明了 MainViewFactory，没有关停回调，所以 App 里挂不上。
    /// </para>
    /// <para>
    /// 必须判 <c>IsFinishing</c>：配置变更（旋转、分屏等）同样会走 OnDestroy，那时单例还要继续用，
    /// 释放掉会让重建的界面拿到已释放的设备。
    /// </para>
    /// </remarks>
    protected override void OnDestroy()
    {
        if (IsFinishing)
        {
            ContainerLocator.Current
                .Resolve<BiometricDeviceManager>()
                .DisposeAsync()
                .GetAwaiter()
                .GetResult();
        }

        base.OnDestroy();
    }
}