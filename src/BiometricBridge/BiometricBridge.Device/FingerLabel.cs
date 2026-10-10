using BiometricBridge.Core.Models.Enums;

namespace BiometricBridge.Device;

/// <summary>
/// 由手别与分割序号推出采集部位
/// </summary>
/// <remarks>
/// <para>
/// 分割器只说"这是左手还是右手"和"这是第几枚"，不给部位码，部位只能这样推。
/// </para>
/// <para>
/// 序号按规范给多指设备定的顺序：食指 → 中指 → 无名指 → 小指（拇指不参与联采）。
/// <b>分割器是否真按这个顺序输出，须真机核对</b>；若核出来不是，只需改这里的表。
/// 另注意原生 <c>fingerInfo</c> 每指的第 4 个 <c>int</c> 叫"类型"，若实测它就是部位码，
/// 应改用它替代"按序号推"（见 <c>IctFlatSegApi</c> 的备注）。
/// </para>
/// <para>
/// 纯映射，不碰原生：单列出来是为了能脱离真机直接断言。
/// </para>
/// </remarks>
internal static class FingerLabel
{
    /// <summary>
    /// 推部位
    /// </summary>
    /// <param name="index">
    /// 分割输出序号（0 起）
    /// </param>
    /// <param name="hand">
    /// 分割器判出的手别
    /// </param>
    /// <param name="group">
    /// 请求的分组
    /// </param>
    /// <returns>
    /// 推出的部位；推不出为 <see cref="BiometricPosition.Unknown"/>
    /// </returns>
    internal static BiometricPosition Of(int index, FingerHand hand, CaptureGroup group)
    {
        // 双拇指跨左右手：手别出参在这一组上没有意义（两枚分属两侧），按"两枚 = 左拇指 + 右拇指"配对。
        if (group == CaptureGroup.Both)
        {
            return index switch
            {
                0 => BiometricPosition.LeftThumb,
                1 => BiometricPosition.RightThumb,
                _ => BiometricPosition.Unknown,
            };
        }

        // 分组指定了左右就按它；只给了"不指定"时，以分割器判出的手别为准。
        var side = group switch
        {
            CaptureGroup.Left => FingerHand.Left,
            CaptureGroup.Right => FingerHand.Right,
            _ => hand,
        };

        return (side, index) switch
        {
            (FingerHand.Left, 0) => BiometricPosition.LeftIndexFinger,
            (FingerHand.Left, 1) => BiometricPosition.LeftMiddleFinger,
            (FingerHand.Left, 2) => BiometricPosition.LeftRingFinger,
            (FingerHand.Left, 3) => BiometricPosition.LeftLittleFinger,
            (FingerHand.Right, 0) => BiometricPosition.RightIndexFinger,
            (FingerHand.Right, 1) => BiometricPosition.RightMiddleFinger,
            (FingerHand.Right, 2) => BiometricPosition.RightRingFinger,
            (FingerHand.Right, 3) => BiometricPosition.RightLittleFinger,
            _ => BiometricPosition.Unknown,
        };
    }
}
