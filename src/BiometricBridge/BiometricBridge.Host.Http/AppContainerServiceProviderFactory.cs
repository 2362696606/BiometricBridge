using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace BiometricBridge.Host.Http;

/// <summary>
/// 让 Web 宿主用应用自己的 DryIoc 容器解析服务
/// </summary>
/// <remarks>
/// <para>
/// 没有它，Web 宿主会自带一套 MS.DI 容器，与应用容器互不相干：控制器要的每个应用单例都只能靠手动登记
/// 实例来搭桥，漏一个就在宿主里多出一份新实例 —— 单例在应用与对外服务之间必须同一份，靠人工维持必然出错。
/// 交给宿主一个工厂，这个约束就由容器结构本身保证。
/// </para>
/// <para>
/// <b>用 Clone 而非 <see cref="RegistrySharing.Share"/></b>：MS.DI 那套规则只加在注册表的副本上，应用容器继续
/// 保有 Prism 自己的规则。跨容器单例同一性不靠 Share 保证 —— 适配器会把应用容器的 SingletonScope 交给副本，
/// 仅凭这一条两边解析到的就是同一个实例。
/// </para>
/// <para>
/// <b>改走 <see cref="NonOwningServiceProvider"/> 而非把容器直接交出去</b>：见该类型的说明 —— 宿主收摊会释放
/// 它拿到的服务提供者，而释放应用容器的 SingletonScope 会连应用的单例一起带走。
/// </para>
/// <para>
/// 每次启动都在此刻对注册表做一次快照，故宿主启动后应用侧再新增的登记要等下次启动才可见。当前登记全在
/// <c>App.RegisterTypes</c> 里、早于任何一次启动完成，故不构成问题。
/// </para>
/// </remarks>
internal sealed class AppContainerServiceProviderFactory : IServiceProviderFactory<IContainer>
{
    #region Fileds

    /// <summary>
    /// 应用容器，宿主的一切解析都归到它
    /// </summary>
    private readonly IContainer _container;

    #endregion

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="container">
    /// 应用容器，由应用侧注入
    /// </param>
    public AppContainerServiceProviderFactory(IContainer container) => _container = container;

    /// <inheritdoc/>
    public IContainer CreateBuilder(IServiceCollection services) =>
        _container.WithDependencyInjectionAdapter(services, registrySharing: RegistrySharing.CloneAndDropCache);

    /// <inheritdoc/>
    public IServiceProvider CreateServiceProvider(IContainer containerBuilder) =>
        new NonOwningServiceProvider(containerBuilder);
}
