namespace BiometricBridge.Device;

/// <summary>
/// 一次多指分割的结果
/// </summary>
/// <param name="Hand">
/// 判出的手别；判不出为 <see cref="FingerHand.Unknown"/>。
/// </param>
/// <param name="Fingers">
/// 分割出的单指，条数不定；空集合表示分割失败，调用方应回退到使用整幅原图。
/// </param>
/// <remarks>
/// 手别是<b>整幅图</b>的属性（一次 slap 采集只可能落在同一只手上），故放在这里而不是逐指重复。
/// 双拇指那一组例外：两枚分属左右手，手别无意义，见 <see cref="FingerLabel"/>。
/// </remarks>
public sealed record SegmentResult(FingerHand Hand, IReadOnlyList<SegmentedFinger> Fingers)
{
    /// <summary>
    /// 分割失败：没有可用的单指结果
    /// </summary>
    public static readonly SegmentResult Empty = new(FingerHand.Unknown, []);
}
