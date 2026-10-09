using BiometricBridge.Core;
using BiometricBridge.Host;
using BiometricBridge.Host.Http.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BiometricBridge.Host.Http;

/// <summary>
/// 以 HTTP 方式对外提供服务的宿主
/// </summary>
/// <remarks>
/// <para>
/// 它是 <see cref="IExternalServiceHost"/> 的一种实现，只负责"把服务跑起来"：建 Web 宿主、
/// 接路由、把功能实例交给它。对外提供什么功能由 <c>BiometricBridge.Host</c> 决定，
/// 换一种服务方式（gRPC、命名管道……）只需另写一个实现，本类与功能层都不动。
/// </para>
/// <para>
/// <b>生命周期由调用方手动驱动</b>（不在应用启动时自动起）。两个方法都幂等：重复启动、未启动就停
/// 都是空操作。故这里不必向外暴露"在不在跑"的状态 —— 那是调用方自己的事。
/// </para>
/// </remarks>
public sealed class HttpServiceHost : IExternalServiceHost
{
    #region 常量

    /// <summary>
    /// 监听地址。仅回环：本机调用方可达，外部机器不可达
    /// </summary>
    /// <remarks>
    /// 写 <c>127.0.0.1</c> 而非 <c>localhost</c>，避开双栈解析差异。地址与端口是骨架期的约定，
    /// 后续移入配置。
    /// </remarks>
    private const string ListenUrl = "http://127.0.0.1:5080";

    #endregion

    #region Fileds

    /// <summary>
    /// 由应用容器交给宿主的桥接器状态服务，随宿主一起登记进 Web 宿主的容器
    /// </summary>
    private readonly BridgeStatusService _statusService;

    /// <summary>
    /// 串行化启动与停止。将来界面按钮从 UI 线程调用也安全
    /// </summary>
    /// <remarks>
    /// 不碰 <see cref="SemaphoreSlim.AvailableWaitHandle"/>，因此无需释放 —— 契约得以不必挂
    /// <see cref="IAsyncDisposable"/>。
    /// </remarks>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Web 宿主。<b>非 null 即表示正在运行</b>
    /// </summary>
    /// <remarks>
    /// 惰性构造（在 <see cref="StartAsync"/> 里建），不在构造函数里建：Web 宿主一经释放便不可复用，
    /// 构造期就建出来的话，停一次之后就再也起不来了。
    /// </remarks>
    private WebApplication? _app;

    #endregion

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="statusService">
    /// 桥接器状态服务，由应用容器注入
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="statusService"/> 为 null
    /// </exception>
    public HttpServiceHost(BridgeStatusService statusService)
    {
        ArgumentNullException.ThrowIfNull(statusService);

        _statusService = statusService;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 已在运行：空操作。
            if (_app is not null)
            {
                return;
            }

            var app = BuildApplication();
            try
            {
                await app.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 起不来（如端口被占）就把半成品就地收掉，免得连它占的资源一起漏掉。
                await app.DisposeAsync().ConfigureAwait(false);
                throw;
            }

            // 只有真正起来了才置位。
            _app = app;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 没在跑：空操作。
            if (_app is null)
            {
                return;
            }

            var app = _app;
            _app = null;

            // 先落状态再等收尾：等待期间若有人再调 Stop，看到的就是"已停"。
            await app.StopAsync(cancellationToken).ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 建 Web 宿主：接好功能与路由，但尚未启动监听
    /// </summary>
    /// <returns>
    /// 已就绪、可直接启动的 Web 宿主
    /// </returns>
    /// <remarks>
    /// 用 <c>CreateSlimBuilder</c> 而非 <c>CreateBuilder</c>：后者会带进 IIS 集成、HTTPS 开发证书、
    /// user-secrets、命令行参数等一整套宿主行为，而这里只是嵌进 GUI 进程的一个服务端。
    /// </remarks>
    private WebApplication BuildApplication()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            // 用程序集目录而非相对路径：运行配置的工作目录不一定是输出目录（Rider 默认可能是项目目录）。
            // 顺带这也是日后的配置缝 —— 输出目录里放个 appsettings.json 即可覆盖监听地址。
            ContentRootPath = AppContext.BaseDirectory,

            // 定死环境：免得偶发地以 Development 起来，引入 user-secrets 等不确定性。
            EnvironmentName = Environments.Production,
        });

        // Serilog 是本应用唯一的日志通道，不让 Web 宿主自己再开一路。
        builder.Logging.ClearProviders();

        builder.WebHost.UseUrls(ListenUrl);

        // DryIoc 容器与 Web 宿主的容器互不相干，缝就是这里：把功能实例登记进去。
        // 实例重载下 MS.DI 不管释放，归属仍在应用容器。
        builder.Services.AddSingleton(_statusService);

        // 必须显式加应用部件：入口程序集是 BiometricBridge.Desktop，MVC 默认只扫入口程序集，
        // 不加的话 Host.Http 里的控制器永远发现不了 —— 表现为请求一律 404，且启动期毫无提示。
        builder.Services.AddControllers().AddApplicationPart(typeof(HealthController).Assembly);

        var app = builder.Build();
        app.MapControllers();
        return app;
    }
}
