namespace BiometricBridge.Core.Models;

/// <summary>
/// 图像几何信息。构造 ISO 19794 记录头时需要，
/// 当 <see cref="CaptureResult.Format"/> 已是 ISO 记录时无需重复提供
/// </summary>
/// <param name="Width">
/// 图像宽度（像素）
/// </param>
/// <param name="Height">
/// 图像高度（像素）
/// </param>
/// <param name="BitDepth">
/// 每像素位深（指纹与虹膜为 8，人脸注册为 24）
/// </param>
/// <param name="HorizontalDpi">
/// 水平像素分辨率（DPI）
/// </param>
/// <param name="VerticalDpi">
/// 垂直像素分辨率（DPI）
/// </param>
public sealed record CaptureImageInfo(
    int Width,
    int Height,
    int BitDepth,
    int HorizontalDpi,
    int VerticalDpi);
