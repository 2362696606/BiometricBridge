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