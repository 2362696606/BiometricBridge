namespace BiometricBridge.Core;

/// <summary>
/// 采集跑完了，却一帧可用的都没拿到：镜头／压板前没有东西，或拿到的帧质量分全为 0
/// </summary>
/// <remarks>
/// <para>
/// 与"设备未连接／未就绪"是<b>两回事</b>：设备好着，只是这次没采到。单列一个类型而不是复用
/// <see cref="InvalidOperationException"/>，是因为上层要把两者翻成<b>不同的</b>协议错误码
/// （101 "Unable to detect a biometric object" 与 110 "Device is not ready"）——
/// 理由同 <see cref="DeviceBusyException"/>：靠异常类型区分比靠消息文本可靠。
/// </para>
/// <para>
/// 2026-10-10 的 SBI1009 正是踩在这条上：操作员没在镜头前，IRIS 采了整整 10 秒超时、一帧未得，
/// 而上层只认 <see cref="InvalidOperationException"/>，于是对 CTK 报了"设备未就绪"（110）。
/// </para>
/// </remarks>
/// <param name="message">
/// 描述信息
/// </param>
public sealed class BiometricNotDetectedException(string message) : Exception(message);
