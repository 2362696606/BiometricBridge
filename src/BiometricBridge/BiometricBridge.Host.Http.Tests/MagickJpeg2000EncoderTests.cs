using BiometricBridge.Host.Http.Iso;
using BiometricBridge.Host.Iso;
using ImageMagick;
using JetBrains.Annotations;
using Xunit;

namespace BiometricBridge.Host.Http.Tests;

/// <summary>
/// <see cref="MagickJpeg2000Encoder"/> 的无损性
/// </summary>
/// <remarks>
/// <para>
/// 守的是"产出的码流真的无损"。CTK 对虹膜注册记录会解一次码，并核对解码器是否判为无损 ——
/// 有损码流过不了；而记录头里那个"无损"是我们自己写死的，写错了它看不出来。
/// </para>
/// <para>
/// <b>为什么用"逐像素回环"当证据</b>：码流标记与真实无损并<b>不</b>等价 ——
/// <c>image.Quality = 0</c> 就能产出"标记仍称可逆、质量层却被截断"的码流，只查标记会放过它。
/// 反过来，回环完全一致足以推出用的是可逆（5-3）变换：不可逆变换在当前位深下必然引入误差，
/// 全值域覆盖的图案是藏不住的。
/// </para>
/// <para>
/// 已知局限：解码用的是 Magick.NET 自己，故回环比对不是完全独立的第三方证据。CTK 侧那一次
/// 由它自己的 OpenJPEG 解码器独立验证，是这条链上真正的外部确认。
/// </para>
/// </remarks>
[TestSubject(typeof(MagickJpeg2000Encoder))]
public class MagickJpeg2000EncoderTests
{
    /// <summary>虹膜设备的实际采集尺寸。</summary>
    private const int IrisWidth = 640;

    private const int IrisHeight = 480;

    [Fact]
    public void 全部灰阶原样回环()
    {
        // 16×16 恰好覆盖 0~255 每个取值：任何量化或位深损失都会当场暴露。
        var gray8 = new byte[16 * 16];
        for (var index = 0; index < gray8.Length; index++)
        {
            gray8[index] = (byte)index;
        }

        AssertRoundTrip(gray8, 16, 16);
    }

    [Theory]
    [InlineData(IrisWidth, IrisHeight)]
    [InlineData(33, 17)]
    [InlineData(1, 1)]
    public void 几何不变且逐像素一致(int width, int height)
    {
        AssertRoundTrip(Pattern(width, height), width, height);
    }

    [Fact]
    public void 产出单分量八位灰度图()
    {
        // 若被当作 RGB 写出，分量数变 3、体积膨胀三倍，而 CTK 要的正是 GRAY[8 bit]。
        using var decoded = new MagickImage(Encode(Pattern(16, 16), 16, 16));

        Assert.Equal(1u, decoded.ChannelCount);
        Assert.Equal(8u, decoded.Depth);
    }

    [Fact]
    public void 空像素被拒()
    {
        // 放行会编出零尺寸图像，下游记录里的图像长度写 0，CTK 的失败点离根因太远。
        Assert.Throws<ArgumentException>(() => Encode([], 4, 4));
    }

    [Fact]
    public void 像素数与宽高不符被拒()
    {
        // 通常意味着调用方拿错了帧的尺寸元数据：照常编码会让图像错位，却看起来"编码成功"。
        Assert.Throws<ArgumentException>(() => Encode(new byte[15], 4, 4));
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(4, 0)]
    [InlineData(-1, 4)]
    public void 非正的宽高被拒(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode(new byte[16], width, height));
    }

    #region 辅助

    /// <summary>以抽象类型声明返回，顺带钉住"这个实现确实实现了那个 seam"。</summary>
    private static IJpeg2000Encoder NewEncoder() => new MagickJpeg2000Encoder();

    private static byte[] Encode(byte[] gray8, int width, int height)
        => NewEncoder().Encode(gray8, width, height);

    /// <summary>
    /// 一幅有结构、非常数的灰度图案。常数图会掩盖有损压缩 —— 整幅同一个值即使被压成一块也"看起来"无损。
    /// </summary>
    private static byte[] Pattern(int width, int height)
    {
        var gray8 = new byte[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                gray8[(y * width) + x] = (byte)(((x * 7) + (y * 13) + ((x * y) % 251)) % 256);
            }
        }

        return gray8;
    }

    private static void AssertRoundTrip(byte[] gray8, int width, int height)
    {
        using var decoded = new MagickImage(Encode(gray8, width, height));
        using var pixels = decoded.GetPixels();

        var restored = pixels.ToByteArray("I")
            ?? throw new InvalidOperationException("解码器未能导出 GRAY8 像素。");

        Assert.Equal(width, (int)decoded.Width);
        Assert.Equal(height, (int)decoded.Height);
        Assert.Equal(gray8, restored);
    }

    #endregion
}
