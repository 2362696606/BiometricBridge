using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Core.Models;

/// <summary>
/// 单帧预览图。设备在采集/注册过程中投给界面的实时画面。
/// </summary>
/// <remarks>
/// 与 <see cref="CaptureResult"/> 的分工：那是"采集的产物"（带质量分、可能已是模板），
/// 这是"过程中的画面"（只求能显示，不参与合规）。两者的数据类型因此不同。
/// </remarks>
public sealed record PreviewFrame
{
    /// <summary>
    /// 图像数据，8 位灰度，长度等于 <see cref="Width"/> × <see cref="Height"/>。
    /// </summary>
    /// <remarks>
    /// <b>必须是调用方已拷贝、自己拥有的缓冲。</b>厂商回调给出的指针只在回调期间有效，
    /// 而缓存与界面会在回调返回之后异步读取这里；把指针直接包成数组，或复用同一个
    /// <c>byte[]</c> 反复填充，都会让上层读到撕裂的帧。生产端负责在回调里当场拷走。
    /// </remarks>
    public required byte[] Data { get; init; }

    /// <summary>
    /// 图像宽度（像素）。
    /// </summary>
    public required int Width { get; init; }

    /// <summary>
    /// 图像高度（像素）。
    /// </summary>
    public required int Height { get; init; }

    /// <summary>
    /// 眼别。非眼部模态或设备未指明时为 null。
    /// </summary>
    public required BiometricPosition? Position { get; init; }

    /// <summary>
    /// 该帧的生成时刻。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="CaptureResult.CapturedAt"/> 同理：设备不提供时间，取主机时间。
    /// </remarks>
    public required DateTimeOffset CapturedAt { get; init; }
}
