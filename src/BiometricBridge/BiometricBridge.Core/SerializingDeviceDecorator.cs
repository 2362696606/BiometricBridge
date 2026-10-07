using BiometricBridge.Core.Models;

namespace BiometricBridge.Core;

/// <summary>
/// 串行化装饰器：把对同一台设备的并发调用排成队列，逐个执行。
/// </summary>
/// <remarks>
/// <para>
/// 被装饰的设备自身不做并发保护（见 <see cref="IBiometricDevice"/> 的实现说明），由本装饰器负责。
/// 采集期间底层设备句柄正被原生调用使用，此时若放行 <see cref="DisconnectAsync"/> 去释放该句柄，
/// 原生侧会访问已释放的内存，故连接、断开与采集共用同一把锁，而不只是采集之间互斥。
/// </para>
/// <para>
/// 排队等待由调用方自己的 <see cref="CancellationToken"/> 控制；<see cref="CaptureRequest.Timeout"/>
/// 只在真正开始采集后计时，不含排队时间。
/// </para>
/// <para>
/// 锁不保证严格的先进先出，只保证任一时刻至多一个调用在设备上执行。
/// </para>
/// </remarks>
public sealed class SerializingDeviceDecorator : IBiometricDeviceDecorator
{
    #region Fileds

    /// <summary>
    /// 设备访问互斥锁。
    /// </summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    #endregion

    /// <summary>
    /// 构造装饰器。
    /// </summary>
    /// <param name="innerDevice">
    /// 被装饰的设备。
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="innerDevice"/> 为 null。
    /// </exception>
    public SerializingDeviceDecorator(IBiometricDevice innerDevice)
    {
        ArgumentNullException.ThrowIfNull(innerDevice);
        InnerDevice = innerDevice;
    }

    /// <inheritdoc/>
    public IBiometricDevice InnerDevice { get; }

    /// <inheritdoc/>
    public bool IsConnected => InnerDevice.IsConnected;

    /// <inheritdoc/>
    public string? SerialNo => InnerDevice.SerialNo;

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InnerDevice.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await InnerDevice.DisconnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<CaptureResult>> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await InnerDevice.CaptureAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 释放被装饰的设备。
    /// </summary>
    /// <returns>
    /// 表示异步释放操作的任务。
    /// </returns>
    /// <remarks>
    /// 不释放 <see cref="SemaphoreSlim"/>：它只在访问 <c>AvailableWaitHandle</c> 时才持有非托管句柄，
    /// 本类不使用该成员，因此无可释放资源；而在采集进行中释放它，会让所有排队者抛
    /// <see cref="ObjectDisposedException"/>。
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        await InnerDevice.DisposeAsync().ConfigureAwait(false);
    }
}
