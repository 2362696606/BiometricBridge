namespace BiometricBridge.Host.Dto;

/// <summary>
/// 设备发现的<b>错误态</b>响应（<c>MOSIPDISC /device</c>）里的一条记录
/// </summary>
/// <remarks>
/// <para>
/// 请求非法时（类型大写、拼错、缺失），数组里的元素是<b>这个形状</b>，而不是
/// <see cref="DiscoveryResponse"/>。协议为两种情形各定了一份 schema：
/// CTK 的 <c>DiscoverResponseErrorSchema</c> 只把 <c>error</c> 列为 <c>required</c>，
/// 并要求 <c>errorCode</c> 匹配 <c>^[1-9][0-9][0-9]$</c>（三位、不以 0 开头）、
/// <c>errorInfo</c> 不得为 <c>"Success"</c>。
/// </para>
/// <para>
/// 注意<b>不是 HTTP 错误码</b>：报文本身就是"发现响应的错误态"，HTTP 仍是 200。
/// </para>
/// <para>
/// 只声明一个属性：其余字段在那份 schema 里都是可空的，省略即可，不必填一堆空串。
/// </para>
/// </remarks>
public sealed record DiscoveryErrorResponse
{
    /// <summary>失败原因。</summary>
    public required SbiError Error { get; init; }
}
