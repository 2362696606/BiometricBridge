using System.Text.Json;
using System.Text.Json.Serialization;
using BiometricBridge.Host.Dto.Enum;

namespace BiometricBridge.Host.Dto;

/// <summary>
/// 注册采集请求（<c>RCAPTURE /capture</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 与认证采集请求共用一个路径，按 HTTP 方法分派到各自的处理者。
/// </para>
/// <para>
/// 与认证侧相比少一项：注册采集不涉及认证服务器，故没有那个地址字段。
/// </para>
/// <para>
/// <b>不认未知属性</b>（<see cref="JsonUnmappedMemberHandling.Disallow"/>）：规范的请求 schema 本身就是
/// <c>additionalProperties: false</c>，多出来的成员只可能是请求方写错了字段名 —— 静默丢掉会让一次笔误
/// 变成"照采了一趟"，而客户端拿到的还是成功。CTK 的 SBI1097/SBI1099 发的正是这种形状
/// （把 <c>purpose</c> 改名成 <c>purposeXXX</c>、把 <c>bio[0].count</c> 改名成 <c>countXXX</c>），
/// 它要的是错误响应。
/// </para>
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RegistrationCaptureRequest
{
    /// <summary>采集所处的环境。</summary>
    public DeviceEnvironment? Env { get; init; }

    /// <summary>本次采集的用途。</summary>
    public Purpose? Purpose { get; init; }

    /// <summary>请求方期望的 SBI 版本。</summary>
    /// <remarks>
    /// <b>线格式是单个字符串</b>（如 <c>"0.9.5"</c>），不是数组：响应侧同型（见
    /// <see cref="RegistrationCaptureResponseBiometric.SpecVersion"/>）。数组形态只在
    /// <c>/discover</c>、<c>/info</c> 的响应里出现，别照抄。
    /// </remarks>
    public string? SpecVersion { get; init; }

    /// <summary>本次采集的超时（毫秒）。</summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? Timeout { get; init; }

    /// <summary>采集时刻。</summary>
    public string? CaptureTime { get; init; }

    /// <summary>本次采集的事务号。</summary>
    public string? TransactionId { get; init; }

    /// <summary>要采集的各模态要求。</summary>
    public RegistrationCaptureRequestBio[]? Bio { get; init; }

    /// <summary>供应商自定义参数。</summary>
    /// <remarks>
    /// 协议只把它定义为一个对象、不约束内部结构，故按原样承载、不解释其内容。
    /// 其中的值不是本服务产生的，形态由请求方决定。
    /// </remarks>
    public Dictionary<string, JsonElement>? CustomOpts { get; init; }
}
