using BiometricBridge.Device.Interop;

namespace BiometricBridge.Device;

/// <summary>
/// 基于 <c>ICTFlatSegDll.dll</c>（分割）与 <c>ICTFingerUtil.dll</c>（评分）的实现。
/// </summary>
/// <remarks>
/// 各常量取自参考实现（<c>ictRegistBridgeAPI.cpp</c>）。
/// </remarks>
public class IctSlapSegmenter : ISlapSegmenter
{
    /// <summary>
    /// 期望分割的手指数。
    /// </summary>
    private const int FingerCount = 4;

    /// <summary>
    /// 单指图像宽度（像素）。
    /// </summary>
    private const int SegmentWidth = 300;

    /// <summary>
    /// 单指图像高度（像素）。
    /// </summary>
    private const int SegmentHeight = 400;

    /// <summary>
    /// 分割缓冲区的防御性边长。
    /// </summary>
    /// <remarks>
    /// 头文件注明每根分割指占 <c>640 × 640</c> 字节，而参考实现按
    /// <see cref="SegmentWidth"/> × <see cref="SegmentHeight"/> 步进，两者矛盾。
    /// 缓冲区按两者中的较大者分配以避免写越界；<b>步进仍取 300×400</b>。
    /// 若实测发现 DLL 实际按 640×640 输出，此处步进需一并修正，否则读到的
    /// 是错位的图像数据（不会越界，但内容是错的）。
    /// </remarks>
    private const int SegmentBufferSide = 640;

    /// <summary>
    /// 质量分计算的分块窗口尺寸。
    /// </summary>
    private const int QualityBlockSize = 8;

    /// <summary>
    /// 质量分计算的分块滑动步长。
    /// </summary>
    private const int QualityStride = 4;

    /// <summary>
    /// 质量分计算的低块判定阈值。
    /// </summary>
    private const float QualityLowBlockThresh = 10.0f;

    /// <summary>
    /// 质量分计算的低块占比阈值。
    /// </summary>
    private const float QualityLowBlockRatio = 0.1f;

    /// <inheritdoc/>
    public IReadOnlyList<SegmentedFinger> Segment(byte[] image, int width, int height)
    {
        var fingerInfo = new int[FingerCount * 4];
        var hand = 0;
        var segmentCount = 0;

        var buffer = new byte[FingerCount * SegmentBufferSide * SegmentBufferSide];

        var rc = IctFlatSegApi.ICTFlatFingerSegmentDLL(image, width, height, FingerCount,
            ref hand, ref segmentCount, fingerInfo, buffer);
        if (rc != 0 || segmentCount <= 0)
        {
            return Array.Empty<SegmentedFinger>();
        }

        // 库返回的个数理论上不会超出请求值，越界读取在这里兜住。
        if (segmentCount > FingerCount)
        {
            segmentCount = FingerCount;
        }

        var segmentBytes = SegmentWidth * SegmentHeight;
        var fingers = new List<SegmentedFinger>(segmentCount);

        for (var i = 0; i < segmentCount; i++)
        {
            var segment = new byte[segmentBytes];
            Buffer.BlockCopy(buffer, i * segmentBytes, segment, 0, segmentBytes);

            var quality = 0;
            IctFingerUtilApi.ict_get_finger_quality(segment, SegmentWidth, SegmentHeight,
                QualityBlockSize, QualityStride, QualityLowBlockThresh, QualityLowBlockRatio,
                ref quality);

            fingers.Add(new SegmentedFinger(segment, SegmentWidth, SegmentHeight, quality));
        }

        return fingers;
    }
}
