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
    /// 采集生物特征。设备在质量分达到 <see cref="CaptureRequest.RequestedScore"/> 时自动采集；
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

    /// <summary>
    /// 持续预览：把设备投出的实时图交给 <paramref name="sink"/>，直到被取消或设备侧结束。
    /// </summary>
    /// <param name="sink">
    /// 预览帧接收端。见 <see cref="PreviewFrameSink"/> 的线程约定。
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌。取消即请求停止预览；本方法在流真正结束（或收尾宽限用尽）后才返回。
    /// </param>
    /// <returns>
    /// 表示预览结束的任务。被正常停止时也应正常完成，而不是抛异常 —— 停止是预期操作。
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// 设备未连接，或该设备已有预览在进行。
    /// </exception>
    /// <remarks>
    /// <b>实现方须在整个预览期间持有设备</b>：连接、断开、采集都不得与之并发。底层机制各设备不同
    /// （虹膜是 fire-and-forget 的原生异步流，指纹是按帧轮询取图），但都要求它跑着的时候没人动设备
    /// —— 若本方法在"流已启动"时就返回，调用方会认为设备空闲，随后的断开就会释放掉原生侧仍在使用的
    /// 句柄。本方法返回后设备才恢复可用。
    /// </remarks>
    Task RunPreviewAsync(PreviewFrameSink sink, CancellationToken cancellationToken = default);
}