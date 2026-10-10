using System.Buffers.Binary;
using BiometricBridge.Core.Models;
using BiometricBridge.Core.Models.Enums;
using CoreEnums = BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Host.Iso;

/// <summary>
/// 把一枚指纹的原始灰度图编码为 ISO/IEC 19794-4 指纹图像记录（格式标识 <c>FIR</c>）
/// </summary>
/// <remarks>
/// <para>
/// <b>依据</b>：CTK 的 ISO 校验器（<c>ISOStandardsValidator</c>）对注册采集响应里 <c>bioValue</c>
/// 的校验项 —— 指尖部分按 <b>ISO/IEC 19794-4:2011</b>。布局与各字段长度取自 CTK 文档
/// "List of ISO validations performed by CTK" 的 Finger 表；该表给出的最小记录长度 57 与
/// 下表求和（通用头 16 + 表示头 21 + 图像数据≥20）自洽，故以该表字段顺序为准。
/// </para>
/// <para>
/// <b>采用未压缩图像</b>：设备只给原始灰度图（<see cref="CaptureDataFormat.RawImage"/>），
/// 不产 WSQ/JPEG2000，故 <c>bioValue</c> 内不放压缩数据。字节序一律大端。
/// </para>
/// <para>
/// <b>本表未含宽度/高度/压缩算法等字段</b>：CTK 的校验项里没有它们，故不写入。若日后 CTK 实测要求，
/// 需按标准补入表示头并同步修正下面的偏移。
/// </para>
/// </remarks>
internal static class Iso19794FingerImage
{
    /// <summary>
    /// 通用头长度：格式标识 4 + 版本 4 + 记录长度 4 + 表示数 2 + 认证标志 1 + 不同部位数 1
    /// </summary>
    private const int GeneralHeaderLength = 16;

    /// <summary>
    /// 表示头（不含图像与质量/认证块）长度：
    /// 表示长度 4 + 采集时间 9 + 质量块数 1 + 认证块数 1 + 部位 1 + 表示序号 1 + 图像数据长度 4
    /// </summary>
    private const int RepresentationHeaderLength = 21;

    /// <summary>
    /// 编码一枚指纹图
    /// </summary>
    /// <param name="result">
    /// 采集结果；须为指纹、原始图形态
    /// </param>
    /// <returns>
    /// ISO/IEC 19794-4 记录的字节
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="result"/> 为 null
    /// </exception>
    /// <exception cref="ArgumentException">
    /// 结果不是指纹，或不是可编码的原始图（缺 <see cref="CaptureResult.Image"/>）
    /// </exception>
    public static byte[] Encode(CaptureResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.Modality != CoreEnums.BiometricModality.Finger)
        {
            throw new ArgumentException($"非指纹模态：{result.Modality}。", nameof(result));
        }

        // 本编码器只处理未压缩原始图：ISO 头部需要几何信息，而只有图像类格式才带它。
        if (result.Format != CaptureDataFormat.RawImage || result.Image is not { } image)
        {
            throw new ArgumentException("缺少可编码的原始图（需 Format=RawImage 且带 Image 几何信息）。", nameof(result));
        }

        var pixelData = result.Data;
        var recordLength = GeneralHeaderLength + RepresentationHeaderLength + pixelData.Length;
        var representationLength = RepresentationHeaderLength + pixelData.Length;

        var record = new byte[recordLength];
        var span = record.AsSpan();

        // —— 通用头 ——
        WriteIdentifier(span, "FIR"u8);
        "020\0"u8.CopyTo(span[4..]);
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], (uint)recordLength);
        BinaryPrimitives.WriteUInt16BigEndian(span[12..], 1); // 表示数：一张图一条
        span[14] = 0; // 认证标志：无认证块
        span[15] = 1; // 不同部位数

        // —— 表示头 ——
        var representation = span[GeneralHeaderLength..];
        BinaryPrimitives.WriteUInt32BigEndian(representation, (uint)representationLength);
        IsoDateTime.Write(representation[4..], result.CapturedAt);
        representation[13] = 0; // 质量块数
        representation[14] = 0; // 认证块数
        representation[15] = PositionCode(result.Position);
        representation[16] = 1; // 表示序号
        BinaryPrimitives.WriteUInt32BigEndian(representation[17..], (uint)pixelData.Length);

        pixelData.CopyTo(span[(GeneralHeaderLength + RepresentationHeaderLength)..]);

        return record;
    }

    /// <summary>
    /// 写入 4 字节格式标识（三字符 + <c>0x00</c> 结束符）
    /// </summary>
    /// <param name="destination">
    /// 目标缓冲区，长度至少 4
    /// </param>
    /// <param name="identifier">
    /// 三字节 ASCII 标识（UTF-8 字面量）
    /// </param>
    private static void WriteIdentifier(Span<byte> destination, ReadOnlySpan<byte> identifier)
    {
        identifier.CopyTo(destination);
        destination[3] = 0;
    }

    /// <summary>
    /// 指位 → ISO/IEC 19794-4 的 <c>finger/palm position</c> 码
    /// </summary>
    /// <param name="position">
    /// 采集位置
    /// </param>
    /// <returns>
    /// 位置码；未知或非指纹位置取 0（Unknown）
    /// </returns>
    /// <remarks>
    /// <c>internal</c> 供一致性测试直取：新增采集位置时若漏补这里，测试会当场发现
    /// （否则只会静默编码成 0）。
    /// </remarks>
    internal static byte PositionCode(BiometricPosition? position) => position switch
    {
        BiometricPosition.RightThumb => 1,
        BiometricPosition.RightIndexFinger => 2,
        BiometricPosition.RightMiddleFinger => 3,
        BiometricPosition.RightRingFinger => 4,
        BiometricPosition.RightLittleFinger => 5,
        BiometricPosition.LeftThumb => 6,
        BiometricPosition.LeftIndexFinger => 7,
        BiometricPosition.LeftMiddleFinger => 8,
        BiometricPosition.LeftRingFinger => 9,
        BiometricPosition.LeftLittleFinger => 10,
        _ => 0,
    };
}
