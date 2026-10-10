namespace BiometricBridge.Core;

/// <summary>
/// 设备预览：让指定设备把实时画面源源不断地投给接收端
/// </summary>
/// <remarks>
/// <para>
/// 与 <see cref="IDeviceInventory"/>（看清单）、<see cref="IDeviceCapture"/>（采一次）并列的第三道窄门。
/// 推流端点（<c>STREAM /stream</c>）要用它，而宿主层够不到设备管理器，故契约落在设备抽象层。
/// </para>
/// <para>
/// <b>停止是独立的一步，且必须收尾</b>：预览期间设备被占用（见 <c>IBiometricDevice.RunPreviewAsync</c>），
/// 调用方结束推流后须调 <see cref="StopPreviewAsync"/>，否则设备一直停在预览态，接不了连接/断开/采集。
/// </para>
/// </remarks>
public interface IDevicePreview
{
    /// <summary>
    /// 开始预览，把设备的实时画面投给 <paramref name="sink"/>
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id，取自 <see cref="Models.DeviceSnapshot.DeviceId"/>
    /// </param>
    /// <param name="sink">
    /// 帧接收端。见 <see cref="PreviewFrameSink"/> 的线程约定：由设备线程调用，不得抛异常
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌，只作用于"启动前先停掉旧会话"这一步
    /// </param>
    /// <returns>
    /// 表示启动操作的任务。返回时设备层的流已受理
    /// </returns>
    /// <exception cref="KeyNotFoundException">
    /// 设备 id 不存在
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 设备未连接，或该设备已有预览在进行
    /// </exception>
    Task StartPreviewAsync(Guid deviceId, PreviewFrameSink sink, CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止设备上的预览。可重复调用：没有预览在跑时直接返回
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌
    /// </param>
    /// <returns>
    /// 表示停止操作的任务。返回时设备已静默，可接连接/断开/采集
    /// </returns>
    /// <exception cref="KeyNotFoundException">
    /// 设备 id 不存在
    /// </exception>
    Task StopPreviewAsync(Guid deviceId, CancellationToken cancellationToken = default);
}
