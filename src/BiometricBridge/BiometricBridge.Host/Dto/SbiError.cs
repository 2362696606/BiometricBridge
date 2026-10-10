namespace BiometricBridge.Host.Dto;

/// <summary>
/// SBI 报文里的错误对象。
/// </summary>
/// <remarks>
/// <para>
/// 各端点表达失败都用它，<b>不是异常</b>：失败是协议里的一种正常回应，
/// 一律以成功的 HTTP 响应携带本对象交出。
/// </para>
/// <para>
/// 类型名不叫 <c>ErrorInfo</c>：那个名字被本类型的属性占着，<c>ErrorInfo.ErrorInfo</c>
/// 读起来分不清是类型还是属性。
/// </para>
/// </remarks>
public sealed record SbiError
{
    /// <summary>错误码。</summary>
    /// <remarks>
    /// <b>线格式上是字符串</b>，不是数字。
    /// </remarks>
    public required string ErrorCode { get; init; }

    /// <summary>错误描述。</summary>
    /// <remarks>
    /// 某些错误码的描述在合规测试里被当作枚举比对，故能用约定文案的地方不要自拟。
    /// </remarks>
    public required string ErrorInfo { get; init; }
}
