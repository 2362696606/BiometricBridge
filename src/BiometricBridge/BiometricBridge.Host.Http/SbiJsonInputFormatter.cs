using System.Text.Json;
using BiometricBridge.Host.Dto;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace BiometricBridge.Host.Http;

/// <summary>
/// 接受任意媒体类型的 SBI 请求体格式化器：把"媒体类型准入"这道闸从模型绑定上拆掉
/// </summary>
/// <remarks>
/// <para>
/// <b>为什么不能按媒体类型设闸</b>：SBI 的报文契约里没有 <c>Content-Type</c> 这一项 —— 规范的发现请求只列了
/// <c>HOST</c> 与 <c>EXT</c>，参考实现（MockMDS）是裸 socket 服务、根本不看它。而 MVC 的默认行为是
/// "请求<b>声明了</b> Content-Type 但没有任何格式化器认它 → 415"，于是真机上出现过：客户端发
/// <c>Content-Type: content-type; charset=UTF-8</c>（连合法的 <c>type/subtype</c> 都不是）的发现请求，
/// 被 <c>[ApiController]</c> 在<b>模型绑定阶段</b>当场回 415、动作方法一行都没跑 ——
/// 日志里只有 <c>Executing endpoint</c> → <c>Route matched</c>、缺 <c>Executing action method</c>，
/// 客户端因此拿不到设备清单。推流端点也栽过同一个坑（见 <c>StreamController.Stream</c> 的注）。
/// </para>
/// <para>
/// <b>为什么是换格式化器，而不是去掉 <c>[FromBody]</c></b>：各端点仍要按 DTO 拿请求体（发现要看 <c>type</c>），
/// 照旧走模型绑定最省事；本类只把媒体类型这一道去掉，并沿用 <see cref="SbiJson.Options"/>，
/// 使请求与响应共用同一份报文规矩。
/// </para>
/// <para>
/// <b>代价</b>：报文体不是合法 JSON 时不再回 415，而是按框架常规回 400；体为空时由参数的"可否为空"决定
/// 放行 null 还是回 400。这正合本项目的分层 —— 传输层不判定报文语义，非法取值交回上层，
/// 由它按规范转成"200 + 错误态元素"（如发现端点的 <c>DiscoveryErrorResponse</c>）。
/// </para>
/// </remarks>
public sealed class SbiJsonInputFormatter : InputFormatter
{
    /// <summary>
    /// 构造函数
    /// </summary>
    public SbiJsonInputFormatter()
    {
        // 通配：任何媒体类型都收。MVC 的准入判断走 MediaTypeHeaderValue.IsSubsetOf，
        // 故 "*/*" 能匹配上客户端声明的任何取值（含上面那个不合法的）。
        SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("*/*"));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// 一律放行：本类存在的意义就是不看媒体类型，是否可读由处理结果说话。
    /// </remarks>
    public override bool CanRead(InputFormatterContext context) => true;

    /// <inheritdoc/>
    /// <remarks>
    /// 整体读进内存再解析：SBI 各请求体都是小报文（最大的是采集请求，几百字节到几 KB），
    /// 而"先看空不空、再决定要不要解"需要拿到全部内容才能判断。
    /// </remarks>
    public override async Task<InputFormatterResult> ReadRequestBodyAsync(InputFormatterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        using var buffer = new MemoryStream();
        await context.HttpContext.Request.Body
            .CopyToAsync(buffer, context.HttpContext.RequestAborted)
            .ConfigureAwait(false);

        // 空体是常规情形：规范对 /info 的请求体写的就是 NA，推流请求体也可缺省。
        // 是否放行 null 归框架（取决于参数可否为空与 EmptyBodyBehavior），这里只如实上报。
        if (buffer.Length == 0)
        {
            return context.TreatEmptyInputAsDefaultValue
                ? InputFormatterResult.Success(null)
                : InputFormatterResult.Failure();
        }

        try
        {
            var model = JsonSerializer.Deserialize(
                buffer.GetBuffer().AsSpan(0, (int)buffer.Length),
                context.ModelType,
                SbiJson.Options);

            return InputFormatterResult.Success(model);
        }
        catch (JsonException)
        {
            // 报文不是合法 JSON：按框架常规（400）表达。协议层面的宽容归上层，
            // 不在这里替它猜"这一坨字节想说什么"。
            context.ModelState.TryAddModelError(context.ModelName, "请求体不是合法的 JSON 报文。");
            return InputFormatterResult.Failure();
        }
    }
}
