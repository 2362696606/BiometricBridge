using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BiometricBridge.Common;
using BiometricBridge.Core;
using BiometricBridge.Core.Models;
using BiometricBridge.Device;
using BiometricBridge.Views;
using DryIoc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Prism.Container.DryIoc;
using Prism.DryIoc;
using Prism.Ioc;
using Serilog;

namespace BiometricBridge;

public class App : PrismApplication
{
    private IConfiguration _configuration = new ConfigurationManager();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
        InitConfig();
        ConfigLog();
        base.Initialize();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 释放设备得靠这个钩子：Prism.Avalonia 不释放容器（PrismApplicationBase 没有 Dispose、
            // 也不订阅任何退出事件），DryIoc 更是不支持 IAsyncDisposable，所以容器不会替我们释放。
            // 用 Exit 而非 MainWindow.Closed：Exit 由 DoShutdown 无条件触发，不受 ShutdownMode 影响。
            desktop.Exit += OnApplicationExit;
        }

        if (ApplicationLifetime is IActivityApplicationLifetime singleViewFactoryApplicationLifetime)
        {
            singleViewFactoryApplicationLifetime.MainViewFactory = () =>
            {
                var pageNavigationHost = new PageNavigationHost
                {
                    Page = MainWindow as ContentPage
                };
                return pageNavigationHost;
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// 应用退出：释放设备，把句柄还给厂商 SDK。
    /// </summary>
    /// <param name="sender">
    /// 事件源。
    /// </param>
    /// <param name="e">
    /// 事件参数。
    /// </param>
    /// <remarks>
    /// 退出路径是同步的，故在此阻塞等待。不会死锁：设备释放内部的等待全部跑在线程池上且一律
    /// <c>ConfigureAwait(false)</c>，不会回到 UI 线程。
    /// </remarks>
    private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        Container.Resolve<BiometricDeviceManager>().DisposeAsync().GetAwaiter().GetResult();
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<BiometricDeviceManager>();

        // 装饰器由容器原生接管：装饰器构造函数注入 IBiometricDevice，DryIoc 会把被装饰的实现传进去。
        // 这样新增设备类型时装饰器自动生效，无需在此处逐个登记。
        var container = containerRegistry.GetContainer();
        container.Register<IBiometricDevice, FCK3L>();
        container.Register<IBiometricDevice, EyeIrisDevice>();
        container.Register<IBiometricDevice, SerializingDeviceDecorator>(setup: Setup.Decorator);

        containerRegistry.Register<ISlapSegmenter, IctSlapSegmenter>();
    }

    /// <summary>
    /// 初始化配置
    /// </summary>
    private void InitConfig()
    {
        var configurationManager = new ConfigurationManager();
        configurationManager.AddYamlFile(@".\Configs\serilog.yaml", optional: true, reloadOnChange: true);
        _configuration = configurationManager;
    }

    /// <summary>
    /// 配置日志
    /// </summary>
    private void ConfigLog()
    {
        var loggerConfiguration = new LoggerConfiguration();
        loggerConfiguration.ReadFrom.Configuration(_configuration);
        Log.Logger = loggerConfiguration.CreateLogger();
    }

    protected override AvaloniaObject CreateShell()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime)
        {
            return Container.Resolve<MainWindow>();
        }

        if (ApplicationLifetime is IActivityApplicationLifetime)
        {
            return Container.Resolve<MainView>();
        }

        if (ApplicationLifetime is ISingleViewApplicationLifetime)
        {
            return Container.Resolve<MainView>();
        }

        throw new NotSupportedException("不支持当前平台");
    }
}