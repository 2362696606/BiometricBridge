using DryIoc;
using Microsoft.Extensions.DependencyInjection;

namespace BiometricBridge.Host.Http;

/// <summary>
/// 交给 Web 宿主的服务提供者：解析一律转发给应用容器，自身不持有任何可释放的东西
/// </summary>
/// <remarks>
/// <para>
/// 本类型存在的唯一理由，是<b>让宿主收摊时碰不到应用容器</b>。<c>WebApplication.DisposeAsync()</c>（由
/// <see cref="HttpServiceHost.StopAsync"/> 触发）会释放它的服务提供者；若把应用容器直接交给宿主，那一次释放
/// 会连应用容器的单例一并释放 —— DryIoc 正是靠共享 SingletonScope 这一对象来实现跨容器单例同一性的，
/// 释放它等于释放应用的单例，此后整个应用容器都不可用（再调 <see cref="HttpServiceHost.StartAsync"/> 会抛
/// <c>Error.ContainerIsDisposed</c>）。现象上就是"界面停一次对外服务，设备也跟着没了"。
/// </para>
/// <para>
/// 故本类型刻意<b>不实现 <see cref="IDisposable"/></b>：宿主找不到可释放的东西，收摊只能空过。这一条就是本类型
/// 的全部要点，去掉它就把上面那个坑重新挖开。
/// </para>
/// <para>
/// 只实现 <see cref="IServiceScopeFactory"/> 是因为 MVC 每处理一个请求要开一个 scope；其余 MS.DI 约定的服务
/// （<c>IServiceProviderIsService</c>、<c>IKeyedServiceProvider</c> 等）由容器自己登记，照常经 <see cref="GetService"/>
/// 转发拿到，这里不必一一复述。
/// </para>
/// </remarks>
internal sealed class NonOwningServiceProvider : IServiceProvider, IServiceScopeFactory
{
    #region Fileds

    /// <summary>
    /// DryIoc 容器，解析都落到它身上
    /// </summary>
    private readonly IServiceProvider _container;

    /// <summary>
    /// 容器的 scope 工厂。构造期取一次即定，之后不再解析
    /// </summary>
    private readonly IServiceScopeFactory _scopeFactory;

    #endregion

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="container">
    /// 已按 MS.DI 约定适配过的 DryIoc 容器
    /// </param>
    public NonOwningServiceProvider(IContainer container)
    {
        _container = container;
        _scopeFactory = (IServiceScopeFactory)container.GetService(typeof(IServiceScopeFactory))!;
    }

    /// <inheritdoc/>
    public object? GetService(Type serviceType) => _container.GetService(serviceType);

    /// <inheritdoc/>
    public IServiceScope CreateScope() => _scopeFactory.CreateScope();
}
