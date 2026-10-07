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
    /// <returns>
    /// 分割出的单指结果，条数不定。返回空集合表示分割失败，
    /// 调用方应回退到使用整幅原图。
    /// </returns>
    IReadOnlyList<SegmentedFinger> Segment(byte[] image, int width, int height);
}
