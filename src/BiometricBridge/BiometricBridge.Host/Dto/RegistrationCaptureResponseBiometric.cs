namespace BiometricBridge.Host.Dto;

/// <summary>
/// 注册采集响应里的一个条目。
/// </summary>
/// <remarks>
/// <para>
/// 与认证侧的同名条目相比少两项：没有加密的会话密钥、也没有证书指纹
/// ——注册采集的生物特征不加密，这是两侧条目唯一的区别。
/// </para>
/// </remarks>
public sealed record RegistrationCaptureResponseBiometric
{
    /// <summary>本服务支持的 SBI 版本。</summary>
    /// <remarks>
    /// <b>线格式是单个字符串</b>（如 <c>"0.9.5"</c>），不是数组：数组形态只在 <c>/discover</c>、
    /// <c>/info</c> 的响应里出现（那里表达的是"支持的版本列表"，见
    /// <see cref="DiscoveryResponse.SpecVersion"/>），采集响应里报的是本次实际所用版本。
    /// </remarks>
    public required string SpecVersion { get; init; }

    /// <summary>本次采集的 JWS 串。</summary>
    /// <remarks>
    /// 内容是一段已签名的 JSON，其结构见 <see cref="RegistrationCaptureData"/>——但此处承载的是
    /// 已序列化的文本，不经本类型反序列化。
    /// </remarks>
    public required string Data { get; init; }

    /// <summary>本数据块的哈希。</summary>
    /// <remarks>
    /// 由上一数据块的哈希与本次数据（加密前）的哈希串成，故条目之间有先后依赖。
    /// </remarks>
    public required string Hash { get; init; }

    /// <summary>失败原因；成功时也必须有，取表示成功的那个码。</summary>
    public required SbiError Error { get; init; }
}
