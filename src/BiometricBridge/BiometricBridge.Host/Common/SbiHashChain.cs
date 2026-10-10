using System.Security.Cryptography;

namespace BiometricBridge.Host.Common;

/// <summary>
/// 采集响应里各数据块之间的哈希链（SHA-256，十六进制大写）
/// </summary>
/// <remarks>
/// <para>
/// 规范把各数据块串成一条链，使"同一次事务里的多次采集"彼此绑定：块的 <c>hash</c> 由上一块的
/// <c>hash</c> 与本块（加密前的）生物特征数据哈希串接后再取哈希。
/// </para>
/// <para>
/// <b>串接的是原始字节，不是十六进制字符串</b>。CTK 的 <c>HashUtil.generateHash</c> 先把上一块的
/// <c>hash</c> 十六进制解回 32 字节、把本块数据哈希成 32 字节，再把这两段 32 字节首尾相接取哈希。
/// 把两个十六进制字符串按 ASCII 拼起来（本方法早期的写法）逐字节都对不上，
/// CTK 的 <c>HashValidator</c> 必然判失败 —— 见用例 SBI1104。
/// </para>
/// <para>
/// 首个采集没有上一块，规范约定其 <c>previousHash</c> 为空串，参与计算时取空字节的 SHA-256。
/// </para>
/// </remarks>
internal static class SbiHashChain
{
    /// <summary>
    /// 本链哈希的长度（十六进制字符数）
    /// </summary>
    public const int HashLength = 64;

    /// <summary>
    /// 首个数据块用的 <c>previousHash</c>：空串
    /// </summary>
    /// <remarks>
    /// 规范原文是"首个采集的 previousHash 是空 UTF-8 串的 SHA-256"，但那个值只在<b>计算</b>里用，
    /// 报文里承载的仍是空串 —— CTK 也是这么解释的（见 <see cref="Next"/>）。
    /// </remarks>
    public const string EmptyPreviousHash = "";

    /// <summary>
    /// 是否是本链认得的哈希字面量
    /// </summary>
    /// <param name="hash">
    /// 待判定的字面量
    /// </param>
    /// <returns>
    /// 是 64 个十六进制字符（大小写皆可）返回 true
    /// </returns>
    /// <remarks>
    /// 供校验请求方给的 <c>previousHash</c>：它会被 <see cref="Next"/> 十六进制解码，
    /// 任其是别的形状只会让解码抛异常、把请求方的笔误变成一次 500。
    /// </remarks>
    public static bool IsValidHash(string? hash)
        => hash is { Length: HashLength } && hash.All(Uri.IsHexDigit);

    /// <summary>
    /// 计算下一个数据块的 <c>hash</c>
    /// </summary>
    /// <param name="previousHash">
    /// 上一块的 <c>hash</c>；首块取 <see cref="EmptyPreviousHash"/>
    /// </param>
    /// <param name="data">
    /// 本块（加密前）的生物特征数据
    /// </param>
    /// <returns>
    /// 本块的 <c>hash</c>（十六进制大写）
    /// </returns>
    /// <exception cref="FormatException">
    /// <paramref name="previousHash"/> 非空且不是合法的十六进制 —— 调用方应先用
    /// <see cref="IsValidHash"/> 挡住这种输入。
    /// </exception>
    public static string Next(string previousHash, ReadOnlySpan<byte> data)
    {
        var previous = string.IsNullOrEmpty(previousHash)
            ? SHA256.HashData(ReadOnlySpan<byte>.Empty)
            : Convert.FromHexString(previousHash);

        var current = SHA256.HashData(data);

        Span<byte> chained = stackalloc byte[previous.Length + current.Length];
        previous.CopyTo(chained);
        current.CopyTo(chained[previous.Length..]);

        return Convert.ToHexString(SHA256.HashData(chained));
    }
}
