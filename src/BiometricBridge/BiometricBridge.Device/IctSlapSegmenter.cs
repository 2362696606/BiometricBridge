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
    /// 多指联采的手指数上限。
    /// </summary>
    /// <remarks>
    /// 规范里的联采最多四枚（左手四指／右手四指）；它同时决定缓冲区大小，故不能由请求里的数字
    /// 说了算（见 <see cref="Segment"/>）。未指定期望枚数时也按它算 —— 本传感器就按这个枚数采。
    /// </remarks>
    private const int MaxFingerCount = 4;

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
    public SegmentResult Segment(byte[] image, int width, int height, int expectedFingerCount)
    {
        // 期望枚数来自请求（分组/枚数），而它决定要分配多大的缓冲区，故先在本地夹住：
        // 未指定或越界的取值不能直接拿去算长度。
        var fingerCount = Math.Clamp(
            expectedFingerCount <= 0 ? MaxFingerCount : expectedFingerCount, 1, MaxFingerCount);

        var fingerInfo = new int[fingerCount * 4];
        var hand = 0;
        var segmentCount = 0;

        var buffer = new byte[fingerCount * SegmentBufferSide * SegmentBufferSide];

        var rc = IctFlatSegApi.ICTFlatFingerSegmentDLL(image, width, height, fingerCount,
            ref hand, ref segmentCount, fingerInfo, buffer);
        if (rc != 0 || segmentCount <= 0)
        {
            return SegmentResult.Empty;
        }

        // 库返回的个数理论上不会超出请求值，越界读取在这里兜住。
        if (segmentCount > fingerCount)
        {
            segmentCount = fingerCount;
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

            fingers.Add(new SegmentedFinger(segment, SegmentWidth, SegmentHeight, quality, i));
        }

        return new SegmentResult(ResolveHand(hand), fingers);
    }

    /// <summary>
    /// 原生手别出参 → <see cref="FingerHand"/>
    /// </summary>
    /// <param name="hand">
    /// 原生出参
    /// </param>
    /// <returns>
    /// 手别；非 1/2 一律按判不出算
    /// </returns>
    /// <remarks>
    /// 头文件的取值是 <c>1</c> 左手、<c>2</c> 右手、<c>0</c> 判定失败，其余取值未定义，
    /// 故按"判不出"兜住而不是硬转。
    /// </remarks>
    private static FingerHand ResolveHand(int hand) => hand switch
    {
        1 => FingerHand.Left,
        2 => FingerHand.Right,
        _ => FingerHand.Unknown,
    };
}
