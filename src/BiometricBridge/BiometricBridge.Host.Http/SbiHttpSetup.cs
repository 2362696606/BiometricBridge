using BiometricBridge.Host.Http.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace BiometricBridge.Host.Http;

/// <summary>
/// SBI 对外接口的装配：控制器、请求体绑定规则与中间件顺序，一处定义
/// </summary>
/// <remarks>
/// <para>
/// 宿主建 Web 宿主时调它，集成测试起测试宿主时也调它 —— <b>装的是同一份</b>。若测试自己另装一遍
/// （各自 <c>AddControllers</c>），宿主漏装 <see cref="SbiJsonInputFormatter"/> 这类规则时测试照样全绿，
/// 而那正是要守的东西（见该类的说明）。
/// </para>
/// <para>
/// 中间件顺序是有讲究的，故与控制器放在一起给：
/// <see cref="SbiTrafficLogMiddleware"/> 在最外层 —— 连"没走到控制器"的通信（预检、415、404）也要留痕；
/// <see cref="SbiCorsMiddleware"/> 在它之内、控制器之前 —— SBI 的调用方是浏览器，预检与正式响应都要 CORS 头。
/// </para>
/// </remarks>
public static class SbiHttpSetup
{
    /// <summary>
    /// 装上 SBI 的控制器与请求体绑定规则
    /// </summary>
    /// <param name="services">
    /// 服务集合
    /// </param>
    /// <returns>
    /// MVC 构造器，供继续追加配置
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="services"/> 为 null
    /// </exception>
    public static IMvcBuilder AddSbiControllers(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services
            .AddControllers(options =>
            {
                // 放在最前：请求体绑定不得按媒体类型设闸，见 SbiJsonInputFormatter。
                options.InputFormatters.Insert(0, new SbiJsonInputFormatter());
            })

            // 必须显式加应用部件：入口程序集是 BiometricBridge.Desktop，MVC 默认只扫入口程序集，
            // 不加的话 Host.Http 里的控制器永远发现不了 —— 表现为请求一律 404，且启动期毫无提示。
            .AddApplicationPart(typeof(HealthController).Assembly);
    }

    /// <summary>
    /// 接上 SBI 的中间件与路由
    /// </summary>
    /// <param name="app">
    /// Web 宿主
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="app"/> 为 null
    /// </exception>
    public static void UseSbiPipeline(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseMiddleware<SbiTrafficLogMiddleware>();
        app.UseMiddleware<SbiCorsMiddleware>();
        app.MapControllers();
    }
}
