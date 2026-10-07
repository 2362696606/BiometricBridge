using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Core.Models;

/// <summary>
/// 单条生物特征采集结果
/// </summary>
public sealed record CaptureResult
{
    /// <summary>
    /// 实际采集到的模态
    /// </summary>
    public required BiometricModality Modality { get; init; }

    /// <summary>
    /// 实际采集到的位置。人脸为 null；设备无法判断具体位置时为
    /// <see cref="BiometricPosition.Unknown"/>
    /// </summary>
    public required BiometricPosition? Position { get; init; }

    /// <summary>
    /// 采集数据。是原始图像、压缩图像还是已编码的 ISO 模板，由 <see cref="Format"/> 决定
    /// </summary>
    public required byte[] Data { get; init; }

    /// <summary>
    /// 采集数据的编码格式
    /// </summary>
    public required CaptureDataFormat Format { get; init; }

    /// <summary>
    /// 图像几何信息。仅在需要上层构造 ISO 记录时提供，即图像类格式；
    /// <see cref="Format"/> 已是 ISO 记录时为 null
    /// </summary>
    public CaptureImageInfo? Image { get; init; }

    /// <summary>
    /// 质量分。不同模态使用不同算法、取值范围不同（指纹为 NFIQ，人脸与虹膜为厂商算法），
    /// 由设备按其约定给出，不做归一化
    /// </summary>
    public double QualityScore { get; init; }

    /// <summary>
    /// 采集时刻（设备时间，UTC）
    /// </summary>
    public required DateTimeOffset CapturedAt { get; init; }
}
