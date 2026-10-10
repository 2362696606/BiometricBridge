using BiometricBridge.Host.Common;
using JetBrains.Annotations;
using Xunit;

namespace BiometricBridge.Host.Tests;

/// <summary>
/// <see cref="SbiHashChain"/> 的单元测试
/// </summary>
/// <remarks>
/// <para>
/// 守的是"与 CTK 逐字节一致"。CTK 的 <c>HashUtil.generateHash</c> 先把上一块的 <c>hash</c> 十六进制
/// 解回 32 字节、把本块数据哈希成 32 字节，再把两段<b>原始字节</b>首尾相接取哈希。
/// 拼接方式一旦退回"两个十六进制字符串按 ASCII 拼"，本地看上去仍是个像样的哈希，
/// 却永远对不上 CTK —— 用例 SBI1104 就是这么失败的。
/// </para>
/// <para>
/// 期望值是拿 openssl 按上述定义（原样字节拼接）独立算出来的，不是照着实现抄的，故能真的锁住拼接方式。
/// </para>
/// </remarks>
[TestSubject(typeof(SbiHashChain))]
public class SbiHashChainTests
{
    /// <summary>首块的期望哈希：data = 01 02 03 FF、previousHash = 空串。</summary>
    private const string FirstBlock = "E02DEBF7FB7F745C526806674506B70D4F425DF06D3BB5A149F12BF4FF4BD690";

    /// <summary>接在 <see cref="FirstBlock"/> 之后、data = 30 的期望哈希。</summary>
    private const string SecondBlock = "40966C623466DEE88BDFF8D67E7F91602C7A2D7F2E5AF58CC59A940175C10836";

    [Fact]
    public void 首块取空字节的哈希作前一哈希()
    {
        // 规范："For the first capture the previousHash is the SHA256 hash of an empty UTF-8 string."
        Assert.Equal(FirstBlock, SbiHashChain.Next(SbiHashChain.EmptyPreviousHash, [0x01, 0x02, 0x03, 0xFF]));
    }

    [Fact]
    public void 首块的常量是空串()
    {
        // 报文里承载的是空串；SHA256("") 那一步只在计算里发生（见 Next）。
        Assert.Equal("", SbiHashChain.EmptyPreviousHash);
    }

    [Fact]
    public void 后续块拿上一块的哈希串起来()
    {
        Assert.Equal(SecondBlock, SbiHashChain.Next(FirstBlock, [0x30]));
    }

    [Fact]
    public void 哈希是六十四位大写十六进制()
    {
        // CTK 的采集响应 schema 对 hash 钉了 ^[A-Z0-9]{64}$。
        var hash = SbiHashChain.Next(SbiHashChain.EmptyPreviousHash, [0x00]);

        Assert.Equal(SbiHashChain.HashLength, hash.Length);
        Assert.Equal(hash.ToUpperInvariant(), hash);
        Assert.All(hash, character => Assert.True(Uri.IsHexDigit(character)));
    }

    [Fact]
    public void 认得出自己产出的哈希()
    {
        var hash = SbiHashChain.Next(SbiHashChain.EmptyPreviousHash, [0xAB]);

        Assert.True(SbiHashChain.IsValidHash(hash));
        Assert.True(SbiHashChain.IsValidHash(hash.ToLowerInvariant()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-hash")]
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B85")]  // 少一位
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B8550")] // 多一位
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B85Z")]  // 含非十六进制字符
    public void 认得出不合法的哈希(string? hash)
    {
        Assert.False(SbiHashChain.IsValidHash(hash));
    }
}
