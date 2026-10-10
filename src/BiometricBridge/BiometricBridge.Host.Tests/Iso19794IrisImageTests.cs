using System.Buffers.Binary;
using BiometricBridge.Core.Models;
using BiometricBridge.Core.Models.Enums;
using BiometricBridge.Host.Iso;
using JetBrains.Annotations;
using Xunit;

namespace BiometricBridge.Host.Tests;

/// <summary>
/// <see cref="Iso19794IrisImage"/> 的字节布局
/// </summary>
/// <remarks>
/// <para>
/// 这里的期望值按 MOSIP 编解码库 <c>io.mosip.biometrics.util.iris</c> 的读写顺序逐字段写死，
/// <b>故意用字面量偏移而不是生产代码里的常量</b> —— 常量写错时断言要能跟着发现，
/// 引常量就成了自己证明自己。
/// </para>
/// <para>
/// 值得单测盯住的原因：这份记录以前少写了一整个"眼别数"字节，其后每个字段整体前移一位，
/// 解码器从第一个表示头字段起就全部读错 —— 而本仓任何测试都看不出来，只有 CTK 会报
/// 一个语焉不详的"图像信息不合法"。
/// </para>
/// </remarks>
[TestSubject(typeof(Iso19794IrisImage))]
public class Iso19794IrisImageTests
{
    /// <summary>固定的采集时刻，用来钉住那 9 字节的写法。</summary>
    private static readonly DateTimeOffset CapturedAt =
        new(2026, 10, 10, 5, 43, 24, 905, TimeSpan.Zero);

    /// <summary>假的 JPEG2000 码流：本层只负责装载，不解释内容。</summary>
    private static readonly byte[] Payload = [0xAA, 0xBB, 0xCC, 0xDD];

    /// <summary>码流之外的固定开销：通用头 16 + 表示头 53 + 图像数据长度 4。</summary>
    private const int Overhead = 73;

    [Fact]
    public void 通用头按十六字节写()
    {
        var record = Encode(BiometricPosition.LeftIris, qualityScore: 77);

        // 记录长度是"整个记录"，不是某一个头。
        Assert.Equal(Overhead + Payload.Length, record.Length);
        Assert.Equal((uint)(Overhead + Payload.Length), ReadUInt32(record, 8));

        Assert.Equal([0x49, 0x49, 0x52, 0x00], record[0..4]);   // "IIR\0"
        Assert.Equal([0x30, 0x32, 0x30, 0x00], record[4..8]);   // "020\0"
        Assert.Equal(1, ReadUInt16(record, 12));                // 表示数
        Assert.Equal(0, record[14]);                            // 认证标志
        Assert.Equal(1, record[15]);                            // 眼别数 —— 就是这个字节曾经漏掉
    }

    [Fact]
    public void 表示头按五十三字节写()
    {
        var record = Encode(BiometricPosition.RightIris, qualityScore: 77);

        // 表示长度 = 整个表示（表示头 53 + 图像数据长度字段 4 + 码流）
        Assert.Equal((uint)(53 + 4 + Payload.Length), ReadUInt32(record, 16));

        Assert.Equal([0x07, 0xEA, 10, 10, 5, 43, 24, 0x03, 0x89], record[20..29]); // 2026-10-10T05:43:24.905Z

        Assert.Equal(1, record[29]);                 // 采集设备技术：CMOS/CCD
        Assert.Equal(0, ReadUInt16(record, 30));     // 设备厂商标识
        Assert.Equal(0, ReadUInt16(record, 32));     // 设备型号标识（为 0 时厂商也须为 0）

        Assert.Equal(1, record[34]);                 // 质量块数
        Assert.Equal(77, record[35]);                // 质量分：取本次实测值
        Assert.Equal(0, ReadUInt16(record, 36));     // 质量算法厂商标识
        Assert.Equal(0, ReadUInt16(record, 38));     // 质量算法标识

        Assert.Equal(1, ReadUInt16(record, 40));     // 表示序号
    }

    [Fact]
    public void 图像信息按二十七字节写()
    {
        var record = Encode(BiometricPosition.RightIris, qualityScore: 77);

        Assert.Equal(1, record[42]);                 // 眼别：右眼
        Assert.Equal(0x03, record[43]);              // 图像类型：已裁剪
        Assert.Equal(0x0A, record[44]);              // 图像格式：单色 JPEG2000
        Assert.Equal(0x40, record[45]);              // 属性位：朝向未定义 + 压缩类型"无损"
        Assert.Equal(640, ReadUInt16(record, 46));   // 宽
        Assert.Equal(480, ReadUInt16(record, 48));   // 高
        Assert.Equal(8, record[50]);                 // 位深

        Assert.Equal(0, ReadUInt16(record, 51));     // 量程
        Assert.Equal(0xFFFF, ReadUInt16(record, 53)); // 眼滚转角：未定义
        Assert.Equal(0xFFFF, ReadUInt16(record, 55)); // 滚转角不确定度：未定义

        // 虹膜中心 X/Y 最小最大与直径最小最大：设备不提供定位，六个字段一律"未定义"（0）
        Assert.Equal(new byte[12], record[57..69]);
    }

    [Fact]
    public void 图像数据块原样装载码流()
    {
        var record = Encode(BiometricPosition.LeftIris, qualityScore: 0);

        Assert.Equal((uint)Payload.Length, ReadUInt32(record, 69));
        Assert.Equal(Payload, record[73..]);
    }

    [Fact]
    public void 左眼填二右眼填一()
    {
        Assert.Equal(2, Encode(BiometricPosition.LeftIris, 80)[42]);
        Assert.Equal(1, Encode(BiometricPosition.RightIris, 80)[42]);
    }

    [Fact]
    public void 质量分为零也收()
    {
        // CTK 只要求质量分落在 0~100；超时后取回的最佳帧就可能没有分数。
        Assert.Equal(0, Encode(BiometricPosition.LeftIris, qualityScore: 0)[35]);
    }

    [Fact]
    public void 质量分超出量程时夹到量程内()
    {
        Assert.Equal(100, Encode(BiometricPosition.LeftIris, qualityScore: 123.4)[35]);
        Assert.Equal(0, Encode(BiometricPosition.LeftIris, qualityScore: -3)[35]);
    }

    [Fact]
    public void 位深不是八被拒()
    {
        // CTK 只接受 8 位深的虹膜图。本地当场失败，胜过到了 CTK 才报"图像信息不合法"。
        var result = Result(BiometricPosition.LeftIris) with
        {
            Image = new CaptureImageInfo(640, 480, 24, 200, 200),
        };

        Assert.Throws<ArgumentException>(() => Iso19794IrisImage.Encode(result, Payload));
    }

    [Fact]
    public void 眼别取不到具体值被拒()
    {
        // 注册用途的 eye label 必须是左或右，不接受"未指定"。
        Assert.Throws<ArgumentException>(() => Encode(BiometricPosition.Unknown, 80));
    }

    [Fact]
    public void 缺几何信息被拒()
    {
        var result = Result(BiometricPosition.LeftIris) with { Image = null };

        Assert.Throws<ArgumentException>(() => Iso19794IrisImage.Encode(result, Payload));
    }

    [Fact]
    public void 空码流被拒()
        => Assert.Throws<ArgumentException>(() => Iso19794IrisImage.Encode(Result(BiometricPosition.LeftIris), []));

    [Fact]
    public void 非虹膜模态被拒()
    {
        var result = Result(BiometricPosition.LeftIris) with { Modality = BiometricModality.Finger };

        Assert.Throws<ArgumentException>(() => Iso19794IrisImage.Encode(result, Payload));
    }

    #region 辅助

    private static byte[] Encode(BiometricPosition position, double qualityScore)
        => Iso19794IrisImage.Encode(Result(position) with { QualityScore = qualityScore }, Payload);

    /// <summary>
    /// 一份形态合法的虹膜采集结果。<see cref="CaptureResult.Data"/> 是编码前的原始灰度，
    /// 本层不用它，故给空数组即可。
    /// </summary>
    private static CaptureResult Result(BiometricPosition position) => new()
    {
        Modality = BiometricModality.Iris,
        Position = position,
        Data = [],
        Format = CaptureDataFormat.RawImage,
        Image = new CaptureImageInfo(640, 480, 8, 200, 200),
        QualityScore = 80,
        CapturedAt = CapturedAt,
    };

    private static ushort ReadUInt16(byte[] record, int offset)
        => BinaryPrimitives.ReadUInt16BigEndian(record.AsSpan(offset));

    private static uint ReadUInt32(byte[] record, int offset)
        => BinaryPrimitives.ReadUInt32BigEndian(record.AsSpan(offset));

    #endregion
}
