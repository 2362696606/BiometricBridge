// ReSharper disable InconsistentNaming

namespace BiometricBridge.Core.Models.Enums;

/// <summary>
/// 采集数据的编码格式
/// </summary>
public enum CaptureDataFormat
{
    /// <summary>
    /// 未压缩原始图像。几何信息见 <see cCaptureResultesult.Image"/>
    /// </summary>
    RawImage,

    /// <summary>
    /// JPEG 2000 图像
    /// </summary>
    Jpeg2000,

    /// <summary>
    /// WSQ 图像
    /// </summary>
    Wsq,

    /// <summary>
    /// ISO/IEC 19794-2 指纹细节点模板
    /// </summary>
    Iso19794_2,

    /// <summary>
    /// ISO/IEC 19794-4 指纹图像记录
    /// </summary>
    Iso19794_4,

    /// <summary>
    /// ISO/IEC 19794-5 人脸图像记录
    /// </summary>
    Iso19794_5,

    /// <summary>
    /// ISO/IEC 19794-6 虹膜图像记录
    /// </summary>
    Iso19794_6,
}
