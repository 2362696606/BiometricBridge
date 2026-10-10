using System.Text.Json;
using BiometricBridge.Host;
using BiometricBridge.Host.Dto;
using Microsoft.AspNetCore.Mvc;

namespace BiometricBridge.Host.Http.Controllers;

/// <summary>
/// SBI 注册采集。注册客户端（如 MOSIP 注册客户端 / CTK）用它采集生物特征
/// </summary>
/// <remarks>
/// <para>
/// 路由写死为规范的 <c>/capture</c>；跨域与预检由 <see cref="SbiCorsMiddleware"/> 统一处理，本类不管。
/// </para>
/// <para>
/// <b>本端点只接注册采集</b>（<c>RCAPTURE</c>）：认证采集（<c>CAPTURE</c>）走另一个动词、另做会话密钥
/// 加密，尚未实现。两版规范对注册采集的动词写法一致，故这里只有 <c>RCAPTURE</c> 一个自定义动词，
/// 另接 <c>POST</c> 以便非浏览器客户端（与 <c>/device</c>、<c>/info</c> 同一态度）。
/// </para>
/// <para>
/// <b>不用 <c>[FromBody]</c> 绑定</b>：绑定失败（体不成 JSON、或含规范没定义的属性）时
/// <c>[ApiController]</c> 会自动回 <b>400 ProblemDetails</b>，而规范要求一切失败都以 HTTP 200 携带
/// SBI 的错误信封 —— 那个 body 过不了 CTK 的 <c>ErrorCaptureResponseSchema</c>（见用例
/// SBI1097/SBI1099）。手工读体才能自己握住这条失败路径，做法与 <see cref="StreamController"/>
/// 同一考虑。
/// </para>
/// <para>
/// 采集请求可能耗时到超时，故动作是异步的，并把 <c>RequestAborted</c> 透传给服务 —— 客户端断开时
/// 能及时取消设备侧采集。
/// </para>
/// </remarks>
[ApiController]
[Route("capture")]
public sealed class CaptureController(RegistrationCaptureService capture, HttpHostSettings settings) : ControllerBase
{
    /// <summary>
    /// 执行注册采集
    /// </summary>
    /// <param name="cancellationToken">
    /// 请求中止令牌（由框架注入 <c>RequestAborted</c>）
    /// </param>
    /// <returns>
    /// 采集响应；失败时为单元素、其错误对象有值的响应
    /// </returns>
    [HttpPost]
    [AcceptVerbs("RCAPTURE")]
    public async Task<ActionResult<RegistrationCaptureResponse>> Capture(CancellationToken cancellationToken)
    {
        // 规范要求采集响应不可缓存，并给出自身地址。
        Response.Headers.CacheControl = "no-store";
        Response.Headers.Location = $"{settings.BaseUrl}/capture";

        RegistrationCaptureRequest request;
        try
        {
            request = await ReadRequestAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            // 体不是 JSON，或含规范没定义的属性。把它当成"合法报文里的一次失败"交出：
            // 带上是哪个属性的原因，好让请求方自己看出笔误在哪。
            return Ok(RegistrationCaptureService.InvalidRequest($"Invalid capture request: {exception.Message}"));
        }

        return Ok(await capture.CaptureAsync(request, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// 读采集请求体
    /// </summary>
    /// <param name="cancellationToken">
    /// 取消令牌
    /// </param>
    /// <returns>
    /// 解析出的请求；确实没有体时返回空请求，交给服务按既有语义报错
    /// </returns>
    /// <exception cref="JsonException">
    /// 体不成 JSON，或含规范没定义的属性（<see cref="RegistrationCaptureRequest"/> 上
    /// 标了 <c>JsonUnmappedMemberHandling.Disallow</c>）
    /// </exception>
    /// <remarks>
    /// 不看 <c>Content-Type</c>，直接按 JSON 试解 —— 与 <see cref="StreamController"/> 同一态度：
    /// 请求体是不是 JSON 由内容说了算，不靠头字段声明。
    /// </remarks>
    private async Task<RegistrationCaptureRequest> ReadRequestAsync(CancellationToken cancellationToken)
    {
        // 有长度且为 0：确实没有体。长度为 null（分块传输）时仍试读一把，读不出自然抛给调用方。
        if (Request.ContentLength == 0)
        {
            return new RegistrationCaptureRequest();
        }

        return await JsonSerializer
            .DeserializeAsync<RegistrationCaptureRequest>(Request.Body, SbiJson.Options, cancellationToken)
            .ConfigureAwait(false)
            ?? new RegistrationCaptureRequest();
    }
}
