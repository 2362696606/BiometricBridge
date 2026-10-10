namespace BiometricBridge.Device;

/// <summary>
/// 多指图像分割器：把一次采集到的、可能含多指的原图拆成按指的结果。
/// </summary>
/// <remarks>
/// 只处理图像数据，不接触设备句柄。
/// </remarks>
public interface ISlapSegmenter
{
    /// <summary>
    /// 分割多指图像，并逐指重新计算质量分。
    /// </summary>
    /// <param name="image">
    /// 灰度图数据，长度为 width × height。
    /// </param>
    /// <param name="width">
    /// 图像宽度。
    /// </param>
    /// <param name="height">
    /// 图像高度。
    /// </param>
    /// <param name="expectedFingerCount">
    /// 期望分割出的手指数，由请求的分组与枚数定（左手四指／右手四指为 4，双拇指为 2）。
    /// 传 0 视为不指定。
    /// </param>
    /// <returns>
    /// 判定出的手别与分割出的单指结果，条数不定。<see cref="SegmentResult.Fingers"/> 为空表示
    /// 分割失败，调用方应回退到使用整幅原图。
    /// </returns>
    SegmentResult Segment(byte[] image, int width, int height, int expectedFingerCount);
}
