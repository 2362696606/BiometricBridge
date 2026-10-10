namespace BiometricBridge.Host.Iso;

/// <summary>
/// JPEG2000 编码失败
/// </summary>
/// <remarks>
/// <para>
/// 单列一个类型，是为了让"编不出来"与"入参不合法"（<see cref="ArgumentException"/>）在上层能分开接
/// —— 前者是编码器的锅，后者是调用方的锅，两者的排查方向完全不同。
/// </para>
/// <para>
/// <b>不许静默降级为有损格式。</b>一次降级会让 <c>bioValue</c> 声称无损而实为有损，而肉眼与记录头
/// 都看不出来 —— 宁可如实失败。
/// </para>
/// </remarks>
public sealed class Jpeg2000EncodingException : Exception
{
    /// <summary>
    /// 构造异常
    /// </summary>
    public Jpeg2000EncodingException()
    {
    }

    /// <summary>
    /// 构造异常
    /// </summary>
    /// <param name="message">
    /// 描述
    /// </param>
    public Jpeg2000EncodingException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// 构造异常
    /// </summary>
    /// <param name="message">
    /// 描述
    /// </param>
    /// <param name="innerException">
    /// 触发它的底层异常
    /// </param>
    public Jpeg2000EncodingException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
