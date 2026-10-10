using System.Text.Json;
using System.Text.Json.Serialization;
using BiometricBridge.Host.Dto.Enum;

namespace BiometricBridge.Host.Dto;

/// <summary>
/// 认证采集请求（<c>CAPTURE /capture</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 与注册采集请求共用一个路径，按 HTTP 方法分派到各自的处理者，故两个请求类型也各有一套
/// ——它们的响应外壳并不相同。
/// </para>
/// <para>
/// <see cref="DomainUri"/> 是认证侧独有的：注册采集不涉及认证服务器。
/// </para>
/// </remarks>
public sealed record AuthCaptureRequest
{
    /// <summary>采集所处的环境。</summary>
    public DeviceEnvironment? Env { get; init; }

    /// <summary>本次采集的用途。</summary>
    public Purpose? Purpose { get; init; }

    /// <summary>请求方期望的 SBI 版本。</summary>
    /// <remarks>
    /// <b>线格式是单个字符串</b>（如 <c>"0.9.5"</c>），不是数组：响应侧同型（见
    /// <see cref="AuthCaptureResponseBiometric.SpecVersion"/>）。数组形态只在 <c>/discover</c>、
    /// <c>/info</c> 的响应里出现，别照抄。
    /// </remarks>
    public string? SpecVersion { get; init; }

    /// <summary>本次采集的超时（毫秒）。</summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? Timeout { get; init; }

    /// <summary>采集时刻。</summary>
    public string? CaptureTime { get; init; }

    /// <summary>认证服务器的地址。注册采集没有这一项。</summary>
    public string? DomainUri { get; init; }

    /// <summary>本次采集的事务号。</summary>
    public string? TransactionId { get; init; }

    /// <summary>要采集的各模态要求。</summary>
    public AuthCaptureRequestBio[]? Bio { get; init; }

    /// <summary>供应商自定义参数。</summary>
    /// <remarks>
    /// 协议只把它定义为一个对象、不约束内部结构，故按原样承载、不解释其内容。
    /// 其中的值不是本服务产生的，形态由请求方决定。
    /// </remarks>
    public Dictionary<string, JsonElement>? CustomOpts { get; init; }
}
