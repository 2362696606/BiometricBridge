using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Core.Models;

/// <summary>
/// 采集请求
/// </summary>
public sealed record CaptureRequest
{
    /// <summary>
    /// 要采集的模态
    /// </summary>
    public required BiometricModality Modality { get; init; }

    /// <summary>
    /// 采集位置。人脸传 null（该维度不适用）；指纹与虹膜传具体位置，
    /// 或传 <see cref="BiometricPosition.Unknown"/> 表示不指定、由设备自行选择。
    /// 取值需与 <see cref="Modality"/> 匹配（指纹位置不可用于虹膜，反之亦然）
    /// </summary>
    public BiometricPosition? Position { get; init; }

    /// <summary>
    /// 超时（毫秒）。超过此时长仍未达质量分，返回本次采集中质量最佳的帧
    /// </summary>
    public int Timeout { get; init; }

    /// <summary>
    /// 期望质量分，达到即自动采集；null 表示使用设备默认阈值
    /// </summary>
    public double? RequestedScore { get; init; }
}
