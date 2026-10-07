using BiometricBridge.Core.Models;

namespace BiometricBridge.Core;

/// <summary>
/// 生物识别设备。直接对接厂商 SDK，只做设备抽象。
/// 不涉及 MOSIP/SBI 协议概念（事务 ID、会话密钥、加密、签名等均由上层负责）
/// </summary>
public interface IBiometricDevice : IAsyncDisposable
{
    /// <summary>
    /// 连接设备
    /// </summary>
    /// <param name="cancellationToken">
    /// 取消令牌
    /// </param>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 断开连接
    /// </summary>
    /// <param name="cancellationToken">
    /// 取消令牌
    /// </param>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 是否已连接。读取本地缓存的连接状态，不探测硬件，因此为同步方法
    /// </summary>
    /// <returns>
    /// 已连接返回 true
    /// </returns>
    bool IsConnected { get; }
    
    /// <summary>
    /// 设备SN码
    /// </summary>
    string? SerialNo { get; }

    /// <summary>
    /// 采集生物特征。设备在质量分达到 <see cref="CaptuCaptureRequestestedScore"/> 时自动采集；
    /// 超时仍未达标时返回本次采集中质量最佳的帧
    /// </summary>
    /// <param name="request">
    /// 采集请求
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌
    /// </param>
    /// <returns>
    /// 采集结果集合。一次调用可能返回多条（如四指联采）
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// 设备未连接
    /// </exception>
    Task<IReadOnlyList<CaptureResult>> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken = default);
}