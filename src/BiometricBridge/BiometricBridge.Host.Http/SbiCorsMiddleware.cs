using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace BiometricBridge.Host.Http;

/// <summary>
/// SBI 的跨域处理：给所有响应带上 CORS 头，并把预检就地答掉
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么是中间件，而不是给每个动作各写一个 OPTIONS</b>：SBI 的调用方是<b>浏览器</b> ——
/// CTK 的页面（如 <c>https://compliance.synergy.mosip.net</c>）里的脚本直接访问 <c>127.0.0.1</c>，
/// 于是每个自定义动词的请求前面都会自动跟一次预检，而且<b>正式响应</b>也必须带 CORS 头，
/// 否则脚本读得到状态码也读不到内容。集中一处，新端点不必各配一份。
/// </para>
/// <para>
/// <b>允许的方法必须列出客户端要用的自定义动词</b>（<c>MOSIPDISC</c>、<c>MOSIPDINFO</c>……）：
/// 少一个，浏览器会把那条正式请求整个拦下 —— 现象是"预检成功、正式请求永不发出"，
/// 从服务端日志里看不出来（只有一条成功的预检）。
/// </para>
/// <para>
/// 页面在公网、目标是回环地址，属 Private Network Access，故一并声明
/// <c>Access-Control-Allow-Private-Network</c>。
/// </para>
/// </remarks>
public sealed class SbiCorsMiddleware(RequestDelegate next, ILogger<SbiCorsMiddleware> logger)
{
    #region 常量

    /// <summary>
    /// 允许的方法
    /// </summary>
    /// <remarks>
    /// 自定义动词由客户端按协议发出，必须逐个列出。
    /// </remarks>
    private const string AllowedMethods = "MOSIPDISC, MOSIPDINFO, MOSIPINFO, RCAPTURE, STREAM, POST, OPTIONS";

    /// <summary>
    /// 允许的请求头
    /// </summary>
    /// <remarks>
    /// <c>EXT</c> 是非浏览器客户端标识自己的头（浏览器客户端用 <c>Origin</c>）。
    /// </remarks>
    private const string AllowedHeaders = "Content-Type, Authorization, EXT, Cache-Control";

    /// <summary>
    /// 预检结果可缓存秒数
    /// </summary>
    private const string MaxAgeSeconds = "5000";

    #endregion

    /// <summary>
    /// 处理请求
    /// </summary>
    /// <param name="context">
    /// 请求上下文
    /// </param>
    /// <returns>
    /// 表示处理完成的任务
    /// </returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var headers = context.Response.Headers;
        var origin = context.Request.Headers.Origin.ToString();

        // 无 Origin 时给 *：非浏览器客户端（curl、CTK 的本地组件）不带它，此时若无许可可给，
        // 报文就白发了。回显 Origin 时声明 Vary，免得缓存把某个来源的响应复用给别的来源。
        headers.AccessControlAllowOrigin = string.IsNullOrEmpty(origin) ? "*" : origin;
        headers.Vary = "Origin";
        headers.AccessControlAllowMethods = AllowedMethods;
        headers.AccessControlAllowHeaders = AllowedHeaders;
        headers.AccessControlMaxAge = MaxAgeSeconds;
        headers["Access-Control-Allow-Private-Network"] = "true";

        if (HttpMethods.IsOptions(context.Request.Method))
        {
            // 预检就地答掉，不进路由：协议要的是"能不能发"，与具体端点无关。
            // 请求方记进日志 —— 这是"同意"流程唯一能留下"谁在请求访问设备"的地方。
            logger.LogInformation(
                "收到 SBI 预检：Origin={Origin}，Access-Control-Request-Method={RequestedMethod}，" +
                "Access-Control-Request-Headers={RequestedHeaders}，User-Agent={UserAgent}。",
                origin,
                context.Request.Headers.AccessControlRequestMethod.ToString(),
                context.Request.Headers.AccessControlRequestHeaders.ToString(),
                context.Request.Headers.UserAgent.ToString());

            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        await next(context).ConfigureAwait(false);
    }
}
