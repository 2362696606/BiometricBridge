using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using BiometricBridge.Common;
using BiometricBridge.Core;
using BiometricBridge.Core.Models;
using BiometricBridge.Device;
using BiometricBridge.Host;
using BiometricBridge.Host.Config;
using BiometricBridge.Host.Crypto;
using BiometricBridge.Host.Http;
using BiometricBridge.Host.Http.Iso;
using BiometricBridge.Host.Iso;
using BiometricBridge.ViewModels;
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

    /// <summary>
    /// MOSIP 侧设置。在 <see cref="InitConfig"/> 里由 sbi.yaml 装填，在 <see cref="RegisterTypes"/> 里交给容器。
    /// </summary>
    private MosipOptions _mosipOptions = new();

    /// <summary>
    /// 进程内日志流。在 <see cref="ConfigLog"/> 里由 <see cref="LogStreamSink"/> 接手写入，在 <see cref="RegisterTypes"/>
    /// 里登记给容器，于是日志视图与日志写入看到的是同一个实例。
    /// </summary>
    /// <remarks>
    /// 由 App 持有实例而非交给容器构造：日志管线必须在 <c>base.Initialize()</c> 之前就绪 —— 容器、壳与设备装饰器
    /// 都在那里面构造，而壳构造时会用上日志（日志视图要快照存量，装饰器要取 logger）。那一刻容器还不存在，
    /// 因此只能先建实例、再登记。
    /// </remarks>
    private readonly LogStreamService _logStream = new();

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
    /// <para>
    /// 先让帧流闭嘴再放设备：释放期间若还有在飞的推帧，帧流已停，不会再往正在拆的界面上抛事件。
    /// 设备侧那头由管理器收（见 <see cref="BiometricDeviceManager.DisposeAsync"/>）。
    /// </para>
    /// </remarks>
    private void OnApplicationExit(object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        // 先掐对外服务再拆设备：它若在跑，正驱动着设备干活，得先让它闭嘴。
        // 当前没有地方调过 StartAsync，故这里必然是空操作（Stop 幂等）；等界面开关落地后，
        // 它就是那道"忘了停就退出"的安全网 —— 不至于把还在监听的端口带进进程收尾。
        try
        {
            Container.Resolve<IExternalServiceHost>().StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "停止对外服务时出错，忽略并继续退出。");
        }

        Container.Resolve<PreviewFrameStream>().Dispose();

        try
        {
            Container.Resolve<BiometricDeviceManager>().DisposeAsync().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            // 在这里抛出去就是崩溃退出。而此刻进程本来就要结束，"原生流可能还活着"已无从补救，
            // 记一条再放行走人。
            Log.Warning(exception, "释放设备时出错，忽略并继续退出。");
        }
    }

    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<BiometricDeviceManager>();

        // 必须是单例：列表与控制面板两个 VM 要共享同一个选中项，transient 会让它们各拿一份。
        containerRegistry.RegisterSingleton<DeviceSelectionService>();

        // 日志流单例：ConfigLog 已让 LogStreamSink 往里写，这里登记同一个实例供日志视图注入。
        // 本项目唯一一处 RegisterInstance —— 实例必须先于容器存在（见 _logStream 的说明），容器只登记不构造。
        containerRegistry.RegisterInstance(_logStream);

        // 预览帧流单例：设备侧拿它当接收端，界面侧从它取帧，两边必须是同一个实例。
        // 也得是单例才能像上面那样在退出路径上显式收尾（容器不替我们释放）。
        containerRegistry.RegisterSingleton<PreviewFrameStream>();

        // 装饰器由容器原生接管：装饰器构造函数注入 IBiometricDevice，DryIoc 会把被装饰的实现传进去。
        // 这样新增设备类型时装饰器自动生效，无需在此处逐个登记。
        var container = containerRegistry.GetContainer();
        container.Register<IBiometricDevice, FCK3L>();
        container.Register<IBiometricDevice, EyeIrisDevice>();

        // 装饰链：order 越大离真实设备越远。串行化贴着设备（在锁内做真正的原生调用），
        // 日志在其外，于是耗时含排队等待，"等锁期间被取消"也能留痕；补帧在最外层，免得补出来的帧
        // 被日志装饰器数进去。
        // 显式给 order 而不靠注册先后：默认 order 为 0 时按注册顺序定层，日后调整注册顺序会悄悄改变装配层级。
        container.Register<IBiometricDevice, SerializingDeviceDecorator>(
            setup: Setup.DecoratorWith(_ => true, order: 0));
        container.Register<IBiometricDevice, LoggingDeviceDecorator>(
            setup: Setup.DecoratorWith(_ => true, order: 1));
        container.Register<IBiometricDevice, PacingDeviceDecorator>(
            setup: Setup.DecoratorWith(_ => true, order: 2));

        // 设备清单：宿主层的设备发现只认 IDeviceInventory，故把该接口映射到上面那个管理器单例。
        // 必须用 RegisterMapping 而不是 RegisterSingleton<IDeviceInventory, BiometricDeviceManager>() ——
        // 后者会让容器再造一个管理器，设备状态与预览随之分成两份。映射不新建实例。
        container.RegisterMapping<IDeviceInventory, BiometricDeviceManager>();

        // 设备采集：注册采集端点经它落到设备上。同样用 RegisterMapping 映射到上面那个管理器单例 ——
        // 采集与清单看到的是同一批设备，不能各造一份。
        container.RegisterMapping<IDeviceCapture, BiometricDeviceManager>();

        // 设备预览：推流端点经它起/停预览。映射到同一管理器单例：预览的交由它仲裁。
        container.RegisterMapping<IDevicePreview, BiometricDeviceManager>();

        containerRegistry.Register<ISlapSegmenter, IctSlapSegmenter>();

        // 对外服务：只登记、不启动。启停是手动操作（由界面开关驱动），
        // 故这里不该有任何地方调用 StartAsync —— 应用起来时不应悄悄占着监听端口。
        // 换一种对外服务方式（gRPC、命名管道……）时，只改下面这一行的实现类型。
        containerRegistry.RegisterSingleton<BridgeStatusService>();

        // SBI 设备发现：控制器经它拼装发现响应，它再经 IDeviceInventory 拿设备清单。无状态，单例即可。
        containerRegistry.RegisterSingleton<DeviceDiscoveryService>();

        // SBI 签名与设备信息：DP 材料用于签发设备证书，故 /info 的 deviceInfo 是签过名的。
        containerRegistry.RegisterInstance(_mosipOptions);

        // DP 签发器懒构造：材料缺失或配置写错时不该在启动期崩，而应表现为 /info 的 500。
        // 单例且不释放 —— JWS 头的 x5c 链里含 DP 证书，它要活满整个进程。
        container.RegisterDelegate<IDeviceCertificateIssuer>(
            resolver => CreateDeviceCertificateIssuer(resolver.Resolve<MosipOptions>()), Reuse.Singleton);

        // 设备密钥按需生成（设备证书的 CN 是序列号，只有连上后才知道），故按身份缓存，见 DeviceSignerProvider。
        container.Register<DeviceSignerProvider>(Reuse.Singleton);
        containerRegistry.RegisterSingleton<DeviceInfoService>();

        // 虹膜图像的 JPEG2000 编码器：实现带平台原生库，故抽象在 Host、实现在 Host.Http，
        // 由这里接上。无状态，单例即可。
        containerRegistry.RegisterSingleton<IJpeg2000Encoder, MagickJpeg2000Encoder>();

        // SBI 注册采集：控制器经它采数据并拼响应。无状态，单例即可。
        containerRegistry.RegisterSingleton<RegistrationCaptureService>();

        // SBI 预览推流：控制器经它选定要推流的设备。无状态，单例即可。
        containerRegistry.RegisterSingleton<PreviewStreamService>();

        // 设备证书的更换入口：界面上的"更换证书"按钮经它走到签发器。
        containerRegistry.RegisterSingleton<DeviceCertificateService>();

        // 监听设置：宿主与界面控制面板共享同一实例 —— 宿主读它建监听地址，面板写它。
        // 必须单例：交给容器按需构造的话两边会各拿一份，面板改的端口到不了宿主。
        containerRegistry.RegisterSingleton<HttpHostSettings>();
        containerRegistry.RegisterSingleton<IExternalServiceHost, HttpServiceHost>();
    }

    /// <summary>
    /// 初始化配置
    /// </summary>
    private void InitConfig()
    {
        var configurationManager = new ConfigurationManager();

        // 用程序集目录而非相对路径：运行配置的工作目录不一定是输出目录（Rider 默认可能是项目目录），
        // 相对路径会让配置文件读不到，而 optional: true 会把这件事静默吞掉 —— 一个 sink 都绑不上。
        configurationManager.AddYamlFile(
            Path.Combine(AppContext.BaseDirectory, "Configs", "serilog.yaml"),
            optional: true,
            reloadOnChange: true);

        // SBI 的 MOSIP 侧设置（DP 材料位置等）。同样 optional：文件缺失时退回 MosipOptions 的内置默认值，
        // 缺材料这件事到首次签名时才有定论，不该在启动期就拦住整个应用。
        configurationManager.AddYamlFile(
            Path.Combine(AppContext.BaseDirectory, "Configs", "sbi.yaml"),
            optional: true,
            reloadOnChange: true);

        _configuration = configurationManager;
        _mosipOptions = BuildMosipOptions(configurationManager);
    }

    /// <summary>
    /// 从配置装填 MOSIP 侧设置。
    /// </summary>
    /// <param name="configuration">
    /// 配置。缺键时退回 <see cref="MosipOptions"/> 的默认值。
    /// </param>
    /// <returns>
    /// MOSIP 侧设置。
    /// </returns>
    private static MosipOptions BuildMosipOptions(IConfiguration configuration)
    {
        var defaults = new MosipOptions();

        return new MosipOptions
        {
            DeviceProviderPath = configuration["mosip:deviceProviderPath"] ?? defaults.DeviceProviderPath,
            DeviceProviderCertFile = configuration["mosip:deviceProviderCertFile"] ?? defaults.DeviceProviderCertFile,
            DeviceProviderKeyFile = configuration["mosip:deviceProviderKeyFile"] ?? defaults.DeviceProviderKeyFile,
            DeviceProviderKeyPassword =
                configuration["mosip:deviceProviderKeyPassword"] ?? defaults.DeviceProviderKeyPassword,
            Env = configuration["mosip:env"] ?? defaults.Env,
        };
    }

    /// <summary>
    /// 建 DP 证书签发器。
    /// </summary>
    /// <param name="options">
    /// MOSIP 侧设置，提供材料位置与私钥口令。
    /// </param>
    /// <returns>
    /// 签发器。
    /// </returns>
    /// <remarks>
    /// 材料目录经 <see cref="SbiMaterialDirectory"/> 解析：工作目录与应用输出目录都试，
    /// 免得 `dotnet run` 与 IDE 启动两种方式里有一种找不到材料。
    /// </remarks>
    private static IDeviceCertificateIssuer CreateDeviceCertificateIssuer(MosipOptions options)
        => new DpCertIssuer(
            SbiMaterialDirectory.Resolve(options.DeviceProviderPath),
            options.DeviceProviderCertFile,
            options.DeviceProviderKeyFile,
            options.DeviceProviderKeyPassword,
            TimeProvider.System);

    /// <summary>
    /// 配置日志
    /// </summary>
    private void ConfigLog()
    {
        var loggerConfiguration = new LoggerConfiguration();
        loggerConfiguration.ReadFrom.Configuration(_configuration);

        // 额外挂一个进程内日志流：给界面一个运行期日志源。与配置文件里的文件 sink 并行，互不影响。
        // 适配器在此构造而不写进 serilog.yaml：它要写入应用自己的服务，配置型 sink 拿不到（见 LogStreamSink 的说明）。
        loggerConfiguration.WriteTo.Sink(new LogStreamSink(_logStream));

        Log.Logger = loggerConfiguration.CreateLogger();

        // 一条启动日志：日志视图在此之前是空的，这行让它有个可见的起点。
        Log.Information("BiometricBridge 启动，运行期日志已就绪");
    }

    protected override AvaloniaObject CreateShell()
    {
        if (Design.IsDesignMode)
        {
            return Container.Resolve<MainWindow>();
        }
        
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