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
/// <param name="Index">
/// 本枚在本次分割输出中的序号（0 起，即原生输出顺序）。用于推出采集部位 ——
/// 分割器不给部位码，只能靠手别加序号推（见 <see cref="FingerLabel"/>）。
/// </param>
public sealed record SegmentedFinger(byte[] Image, int Width, int Height, int QualityScore, int Index);
