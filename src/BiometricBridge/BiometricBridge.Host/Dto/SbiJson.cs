using System.Text.Encodings.Web;
using System.Text.Json;

namespace BiometricBridge.Host.Dto;

/// <summary>
/// SBI 报文共用的 JSON 序列化选项
/// </summary>
/// <remarks>
/// <para>
/// 线上字段名由各 Dto 上的 <c>[JsonPropertyName]</c> 与枚举转换器定死，本选项只管其余规矩。
/// </para>
/// <para>
/// <b>未签名与已签名两种形态必须共用它</b>：客户端会比对同一份载荷的两种容器（例如拿 <c>/device</c> 的
/// 未签名 digitalId 与 <c>/info</c> 内嵌的那份对照，确认"还是刚才那台设备"），序列化一旦分家，
/// 就会表现为"两处不是同一台设备"。
/// </para>
/// </remarks>
public static class SbiJson
{
    /// <summary>
    /// 报文序列化选项
    /// </summary>
    /// <remarks>
    /// 取 Web 默认（camelCase、大小写不敏感）；<see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/>
    /// 让中文说明直接以 UTF-8 输出，不被转义成 <c>\uXXXX</c>。
    /// </remarks>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
