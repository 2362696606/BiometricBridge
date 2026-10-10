namespace BiometricBridge.Host.Dto;

/// <summary>
/// 注册采集响应（<c>RCAPTURE /capture</c>）。
/// </summary>
/// <remarks>
/// <para>
/// 成功与失败共用这一个外壳：失败时 <see cref="Biometrics"/> 里只有一个条目、其错误对象有值。
/// 顶层没有错误对象，成败只由条目里的那个表达。
/// </para>
/// </remarks>
public sealed record RegistrationCaptureResponse
{
    /// <summary>本次采集的各条目，顺序与请求里要求的一致。</summary>
    public required RegistrationCaptureResponseBiometric[] Biometrics { get; init; }
}
