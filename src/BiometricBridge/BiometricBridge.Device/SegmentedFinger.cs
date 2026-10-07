namespace BiometricBridge.Device;

/// <summary>
/// 从多指图像中分割出的单指结果。
/// </summary>
/// <param name="Image">
/// 单指灰度图数据，长度为 <paramref name="Width"/> × <paramref name="Height"/>。
/// </param>
/// <param name="Width">
/// 图像宽度（像素）。
/// </param>
/// <param name="Height">
/// 图像高度（像素）。
/// </param>
/// <param name="QualityScore">
/// 质量分（NFIQ 2.0）。由分割后的单指图像重新计算，不是全图的分值。
/// </param>
public sealed record SegmentedFinger(byte[] Image, int Width, int Height, int QualityScore);
