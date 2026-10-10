namespace BiometricBridge.Host.Dto;

/// <summary>
/// 预览推流的失败报文（<c>STREAM /stream</c>）。
/// </summary>
/// <remarks>
/// <para>
/// <b>本端点只有失败才走这个类型。</b>成功路径是 MJPEG 的帧序列，根本不产生 JSON 报文，
/// 故这里没有"成功时填什么"的问题——只有出错时才用得着它。
/// </para>
/// <para>
/// 也正因为如此，校验必须在本类型有可能被写出之前全部走完：响应头一旦按流的方式发出，
/// 协议里就没有改回 JSON 报文的位置了。
/// </para>
/// </remarks>
public sealed record StreamResponse
{
    /// <summary>失败原因。</summary>
    public required SbiError Error { get; init; }
}
