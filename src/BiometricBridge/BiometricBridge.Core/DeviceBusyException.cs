namespace BiometricBridge.Core;

/// <summary>
/// 设备正忙：上一次采集尚未结束，本次无法受理
/// </summary>
/// <remarks>
/// <para>
/// 规范要求同一台设备的采集"一次只受理一个"，并行调用应<b>当场以 Busy 拒绝</b>，而不是排队等待
/// （"The capture call will respond with success to only one call at a time"）。
/// </para>
/// <para>
/// 单列一个类型而不是复用 <see cref="InvalidOperationException"/>：上层要把"忙"与"未连接/未就绪"
/// 翻成<b>不同的</b>协议错误码（111 与 110），靠异常类型区分比靠消息文本可靠。
/// </para>
/// </remarks>
/// <param name="message">
/// 描述信息
/// </param>
public sealed class DeviceBusyException(string message) : Exception(message);
