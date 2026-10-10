using System.Buffers.Binary;
using BiometricBridge.Core.Models;
using BiometricBridge.Core.Models.Enums;
using CoreEnums = BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Host.Iso;

/// <summary>
/// 把一只眼睛的虹膜图装成 ISO/IEC 19794-6 虹膜图像记录（格式标识 <c>IIR</c>）
/// </summary>
/// <remarks>
/// <para>
/// <b>依据</b>：CTK 的 ISO 校验器（<c>IrisISOStandardsValidator</c>）所逐字段核对的那套布局，
/// 与 MOSIP 自己的编解码库 <c>io.mosip.biometrics.util.iris</c> 完全同源。本类的偏移量逐个对齐
/// 该库的 <c>GeneralHeader</c> / <c>RepresentationHeader</c> / <c>ImageInformation</c> / <c>ImageData</c>
/// 的读写顺序，不是照文档表格抄的。
/// </para>
/// <para>
/// <b>本类只负责装载，不负责编码</b>：记录里的图像数据必须是真正的 JPEG2000 码流，那一步由
/// <see cref="IJpeg2000Encoder"/> 在调用方完成，本类把码流原样放进记录。
/// </para>
/// <para>
/// <b>注册用途的取值</b>（与 MOSIP 参考编码器一致）：图像类型取"已裁剪"、图像格式取"单色
/// JPEG2000"、压缩类型取"无损"。注意 <b>MOSIP 自己的参考编码器也不真的裁剪虹膜</b> —— 它把输入
/// 图像原样放入、只把类型声明为"已裁剪"，故"整幅图 + 声明已裁剪"是参考实现的做法。
/// 虹膜中心与直径因设备不提供定位信息，一律填"未定义"，这也是 ISO 允许的取值。
/// </para>
/// </remarks>
internal static class Iso19794IrisImage
{
    #region 通用头

    /// <summary>通用头长度：格式标识 4 + 版本 4 + 记录长度 4 + 表示数 2 + 认证标志 1 + 眼别数 1</summary>
    /// <remarks>
    /// <b>末尾那个"眼别数"是一个独立的字节</b>，不是"表示数"的一部分 —— 早期实现按 15 字节写，
    /// 少了它，其后每个字段都整体前移一字节，解码器从第一个表示头字段起就全部读错。
    /// </remarks>
    public const int GeneralHeaderLength = 16;

    /// <summary>格式标识 <c>IIR\0</c> 的偏移。</summary>
    public const int FormatIdentifierOffset = 0;

    /// <summary>版本号 <c>020\0</c> 的偏移。</summary>
    public const int VersionNumberOffset = 4;

    /// <summary>记录长度（含整个记录）的偏移。</summary>
    public const int RecordLengthOffset = 8;

    /// <summary>表示数的偏移。</summary>
    public const int NoOfRepresentationsOffset = 12;

    /// <summary>认证标志的偏移。</summary>
    public const int CertificationFlagOffset = 14;

    /// <summary>眼别数的偏移。</summary>
    public const int NoOfEyesPresentOffset = 15;

    #endregion

    #region 表示头

    /// <summary>表示头长度：表示长度 4 + 采集时间 9 + 设备技术 1 + 设备厂商 2 + 设备型号 2 + 质量块数 1 + 质量块 5 + 表示序号 2 + 图像信息 27</summary>
    public const int RepresentationHeaderLength = 53;

    /// <summary>表示长度的偏移。</summary>
    public const int RepresentationLengthOffset = 16;

    /// <summary>采集日期的偏移。</summary>
    public const int CaptureDateTimeOffset = 20;

    /// <summary>采集设备技术标识的偏移。</summary>
    public const int CaptureDeviceTechnologyOffset = 29;

    /// <summary>采集设备厂商标识的偏移。</summary>
    public const int CaptureDeviceVendorOffset = 30;

    /// <summary>采集设备型号标识的偏移。</summary>
    public const int CaptureDeviceTypeOffset = 32;

    /// <summary>质量块数的偏移。</summary>
    public const int NoOfQualityBlocksOffset = 34;

    /// <summary>质量分的偏移。</summary>
    public const int QualityScoreOffset = 35;

    /// <summary>表示序号的偏移。</summary>
    public const int RepresentationNoOffset = 40;

    /// <summary>眼别的偏移。</summary>
    public const int EyeLabelOffset = 42;

    /// <summary>图像类型的偏移。</summary>
    public const int ImageTypeOffset = 43;

    /// <summary>图像格式的偏移。</summary>
    public const int ImageFormatOffset = 44;

    /// <summary>图像属性位（打包成一字节：水平朝向 2 位、垂直朝向 2 位、保留 2 位、压缩类型 2 位）的偏移。</summary>
    public const int ImagePropertiesOffset = 45;

    /// <summary>图像宽度的偏移。</summary>
    public const int WidthOffset = 46;

    /// <summary>图像高度的偏移。</summary>
    public const int HeightOffset = 48;

    /// <summary>位深的偏移。</summary>
    public const int BitDepthOffset = 50;

    /// <summary>虹膜图像量程的偏移。</summary>
    public const int RangeOffset = 51;

    /// <summary>眼滚转角的偏移。</summary>
    public const int RollAngleOfEyeOffset = 53;

    /// <summary>滚转角不确定度的偏移。</summary>
    public const int RollAngleUncertaintyOffset = 55;

    /// <summary>虹膜中心最小 X 的偏移（其后依次为最大 X、最小 Y、最大 Y、直径最小、直径最大，各 2 字节）。</summary>
    public const int IrisCenterOffset = 57;

    #endregion

    #region 图像数据块

    /// <summary>图像数据长度的偏移。</summary>
    public const int ImageDataLengthOffset = 69;

    /// <summary>图像数据的起始偏移。</summary>
    public const int ImageDataOffset = 73;

    #endregion

    #region 取值

    /// <summary>图像类型：已裁剪。</summary>
    private const byte ImageTypeCropped = 0x03;

    /// <summary>图像格式：单色 JPEG2000。</summary>
    private const byte ImageFormatMonoJpeg2000 = 0x0A;

    /// <summary>图像属性位：水平/垂直朝向均"未定义"，压缩类型为"无损"（<c>1 &lt;&lt; 6</c>）。</summary>
    private const byte ImagePropertiesLosslessUndefinedOrientation = 0x40;

    /// <summary>采集设备技术标识：CMOS 或 CCD。</summary>
    private const byte CaptureDeviceTechnologyCmosOrCcd = 0x01;

    /// <summary>未定义（各类"取不到"的字段一律填它）：2 字节时是 <c>0xFFFF</c>。</summary>
    private const ushort Undefined = 0xFFFF;

    /// <summary>CTK 只接受 8 位深的虹膜图。</summary>
    private const int RequiredBitDepth = 8;

    /// <summary>质量分位数。</summary>
    private const int QualityScoreMax = 100;

    #endregion

    /// <summary>
    /// 装载一只眼睛的虹膜记录
    /// </summary>
    /// <param name="result">
    /// 采集结果；提供眼别、采集时刻与几何信息
    /// </param>
    /// <param name="jpeg2000">
    /// 该眼的 JPEG2000 码流，即记录里的图像数据。
    /// </param>
    /// <returns>
    /// ISO/IEC 19794-6 记录的字节
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// 任一参数为 null。
    /// </exception>
    /// <exception cref="ArgumentException">
    /// 结果不是虹膜、缺几何信息、位深不是 8、眼别取不到具体值，或码流为空。
    /// </exception>
    /// <remarks>
    /// <see cref="CaptureResult.Data"/>（编码前的原始灰度）本方法不用 —— 图像数据由
    /// <paramref name="jpeg2000"/> 给出，两者不同源是调用方的错，故只认后者。
    /// </remarks>
    public static byte[] Encode(CaptureResult result, byte[] jpeg2000)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(jpeg2000);

        if (result.Modality != CoreEnums.BiometricModality.Iris)
        {
            throw new ArgumentException($"非虹膜模态：{result.Modality}。", nameof(result));
        }

        if (jpeg2000.Length == 0)
        {
            throw new ArgumentException("JPEG2000 码流为空。", nameof(jpeg2000));
        }

        if (result.Image is not { } image)
        {
            throw new ArgumentException("缺少几何信息（需带 Image）。", nameof(result));
        }

        // 位深与眼别是 CTK 会逐字段核对的取值，取不到就别交出去 —— 本地当场失败，
        // 胜过到了 CTK 才报一个"图像信息不合法"。
        if (image.BitDepth != RequiredBitDepth)
        {
            throw new ArgumentException(
                $"虹膜记录只接受 {RequiredBitDepth} 位深，实际为 {image.BitDepth}。", nameof(result));
        }

        var eyeLabel = EyeLabel(result.Position);
        if (eyeLabel == 0)
        {
            throw new ArgumentException(
                $"注册用途的眼别必须落在左/右，实际为 {result.Position}。", nameof(result));
        }

        var pixelLength = jpeg2000.Length;
        var recordLength = ImageDataOffset + pixelLength;
        var representationLength = recordLength - GeneralHeaderLength;

        var record = new byte[recordLength];
        var span = record.AsSpan();

        // —— 通用头 ——
        "IIR"u8.CopyTo(span[FormatIdentifierOffset..]);
        span[FormatIdentifierOffset + 3] = 0;
        "020"u8.CopyTo(span[VersionNumberOffset..]);
        span[VersionNumberOffset + 3] = 0;
        BinaryPrimitives.WriteUInt32BigEndian(span[RecordLengthOffset..], (uint)recordLength);
        BinaryPrimitives.WriteUInt16BigEndian(span[NoOfRepresentationsOffset..], 1);
        span[CertificationFlagOffset] = 0;

        // 一只眼睛一条表示，故"眼别数"与"表示数"同为 1。
        span[NoOfEyesPresentOffset] = 1;

        // —— 表示头 ——
        BinaryPrimitives.WriteUInt32BigEndian(span[RepresentationLengthOffset..], (uint)representationLength);
        IsoDateTime.Write(span[CaptureDateTimeOffset..], result.CapturedAt);

        span[CaptureDeviceTechnologyOffset] = CaptureDeviceTechnologyCmosOrCcd;

        // 设备厂商与型号标识保持 0（未定义）：型号标识为 0 时厂商标识也须为 0，这是校验器的一条约束。
        BinaryPrimitives.WriteUInt16BigEndian(span[CaptureDeviceVendorOffset..], 0);
        BinaryPrimitives.WriteUInt16BigEndian(span[CaptureDeviceTypeOffset..], 0);

        // 一个质量块：分数取本次采集实际测得的质量分（算法标识为"未定义"，故只作参考值），
        // 厂商与算法标识同样为 0。
        span[NoOfQualityBlocksOffset] = 1;
        span[QualityScoreOffset] = (byte)Math.Clamp(result.QualityScore, 0, QualityScoreMax);
        BinaryPrimitives.WriteUInt16BigEndian(span[(QualityScoreOffset + 1)..], 0);
        BinaryPrimitives.WriteUInt16BigEndian(span[(QualityScoreOffset + 3)..], 0);

        BinaryPrimitives.WriteUInt16BigEndian(span[RepresentationNoOffset..], 1);

        span[EyeLabelOffset] = eyeLabel;
        span[ImageTypeOffset] = ImageTypeCropped;
        span[ImageFormatOffset] = ImageFormatMonoJpeg2000;
        span[ImagePropertiesOffset] = ImagePropertiesLosslessUndefinedOrientation;

        BinaryPrimitives.WriteUInt16BigEndian(span[WidthOffset..], (ushort)image.Width);
        BinaryPrimitives.WriteUInt16BigEndian(span[HeightOffset..], (ushort)image.Height);
        span[BitDepthOffset] = (byte)image.BitDepth;

        BinaryPrimitives.WriteUInt16BigEndian(span[RangeOffset..], 0);
        BinaryPrimitives.WriteUInt16BigEndian(span[RollAngleOfEyeOffset..], Undefined);
        BinaryPrimitives.WriteUInt16BigEndian(span[RollAngleUncertaintyOffset..], Undefined);

        // 虹膜中心与直径：设备不提供定位信息，6 个字段一律填"未定义"（0），ISO 允许。
        span.Slice(IrisCenterOffset, ImageDataLengthOffset - IrisCenterOffset).Clear();

        // —— 图像数据块 ——
        BinaryPrimitives.WriteUInt32BigEndian(span[ImageDataLengthOffset..], (uint)pixelLength);
        jpeg2000.CopyTo(span[ImageDataOffset..]);

        return record;
    }

    /// <summary>
    /// 眼别 → ISO/IEC 19794-6 的 <c>eye label</c>
    /// </summary>
    /// <param name="position">
    /// 采集位置
    /// </param>
    /// <returns>
    /// 眼别：右眼 1、左眼 2、未知 0
    /// </returns>
    /// <remarks>
    /// <c>internal</c> 供一致性测试直取：新增采集位置时若漏补这里，测试会当场发现
    /// （否则只会静默编码成 0）。
    /// </remarks>
    internal static byte EyeLabel(BiometricPosition? position) => position switch
    {
        BiometricPosition.RightIris => 1,
        BiometricPosition.LeftIris => 2,
        _ => 0,
    };
}
