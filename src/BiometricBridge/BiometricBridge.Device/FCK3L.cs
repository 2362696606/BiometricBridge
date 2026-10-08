using System.Diagnostics;
using BiometricBridge.Core;
using BiometricBridge.Core.Models;
using BiometricBridge.Core.Models.Attributes;
using BiometricBridge.Core.Models.Enums;
using BiometricBridge.Device.Interop;

namespace BiometricBridge.Device;

/// <summary>
/// FCK3L 指纹采集设备，经 <c>ictScanAPI.dll</c> 访问。
/// </summary>
/// <remarks>
/// <para>
/// 只支持指纹模态。
/// </para>
/// <para>
/// 不做并发保护：设备并发由外层装饰器负责。
/// </para>
/// <para>
/// 原生函数一律阻塞，故全部丢到线程池执行；库对调用线程无要求。
/// </para>
/// <para>
/// 原图宽度达到 <see cref="SlapWidthMin"/> 时视为多指图像，交由
/// <see cref="ISlapSegmenter"/> 拆成按指结果；分割失败则回退到整幅图。
/// </para>
/// </remarks>
// ReSharper disable once InconsistentNaming
[FingerprintDeviceInfo(
    Make = "ICT Global",
    Model = "FCK3L",
    DeviceProvider = "FTM-Test",
    DeviceProviderId = "FTM-TEST-001",
    DeviceSubIds = [1, 2, 3],
    DeviceSubType = DeviceSubType.Slap,
    Certification = CertificationLevel.L0,
    Purpose = Purpose.Registration)]
// ReSharper disable once InconsistentNaming
public class FCK3L : IBiometricDevice
{
    #region 常量

    /// <summary>
    /// 传感器分辨率（ppi）。
    /// </summary>
    /// <remarks>
    /// 库不提供该值，按 FAP50 标准取 500。
    /// </remarks>
    private const int SensorDpi = 500;

    /// <summary>
    /// 判定为多指图像的最小宽度（像素）。取自参考实现。
    /// </summary>
    private const int SlapWidthMin = 800;

    /// <summary>
    /// 取原始图时传给 SDK 的压缩比。
    /// </summary>
    /// <remarks>
    /// 取参考实现的 <c>1</c>。Raw 格式下这个参数按理无意义，但参考实现是实测能用的，值就跟着对齐。
    /// </remarks>
    private const int RawImageCompressionRatio = 1;

    /// <summary>
    /// 预览取帧的质量门限。<c>0</c> 表示不设要求。
    /// </summary>
    /// <remarks>
    /// 预览要看的是"现在镜头前是什么"，不是一张合格图像；设门限只会让画面卡住等一张合格的。
    /// </remarks>
    private const int PreviewQualityGate = 0;

    /// <summary>
    /// 预览每轮取图的超时（毫秒）。
    /// </summary>
    /// <remarks>
    /// 只作兜底：手指不在、或这一轮设备出不了图时，循环靠它保持节拍，不至于空转。
    /// </remarks>
    private const int PreviewCaptureTimeout = 200;

    /// <summary>
    /// 每轮之间至少歇这么久。
    /// </summary>
    /// <remarks>
    /// 只是兜底：设备正常返回一次要几百毫秒，这个下限根本不起作用；它挡的是"设备调用瞬时返回"
    /// （真掉线时抛得快）把循环变成忙等。取参考实现的 50ms。
    /// <b>不做固定节流</b>：画面更新得够不够密由 <see cref="PacingDeviceDecorator"/> 那道下限兜，
    /// 设备这一侧只管按自己的物理节奏出帧 —— 固定节流只会把设备的节奏再拖慢一截。
    /// </remarks>
    private static readonly TimeSpan PreviewMinInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// 预览连续取不到帧多久，就认为流该收手了。
    /// </summary>
    /// <remarks>
    /// 取不到帧在预览里是常态（没放手指就没有帧），故不能一失败就断流；但设备真掉线时也不能永远干等
    /// —— 这段时限是两者的分界。取参考实现的 3 秒。
    /// </remarks>
    private static readonly TimeSpan PreviewFailureLimit = TimeSpan.FromSeconds(3);

    #endregion

    #region Fileds

    /// <summary>
    /// 设备句柄；<see cref="IntPtr.Zero"/> 表示未连接。
    /// </summary>
    private IntPtr _handle = IntPtr.Zero;

    /// <summary>
    /// 设备信息，连接成功后填充；用于确定图像缓冲区尺寸。
    /// </summary>
    private IctScanDeviceInfo _deviceInfo;

    /// <summary>
    /// 多指图像分割器。
    /// </summary>
    private readonly ISlapSegmenter _segmenter;

    /// <summary>
    /// 设备sn码
    /// </summary>
    private readonly string _serialNo = Guid.NewGuid().ToString();

    #endregion

    /// <summary>
    /// 构造设备。
    /// </summary>
    /// <param name="segmenter">
    /// 多指图像分割器。采集到的原图宽度达到 <see cref="SlapWidthMin"/> 时用它拆成按指结果。
    /// </param>
    public FCK3L(ISlapSegmenter segmenter)
    {
        ArgumentNullException.ThrowIfNull(segmenter);
        _segmenter = segmenter;
    }

    /// <inheritdoc/>
    public bool IsConnected => _handle != IntPtr.Zero;

    /// <inheritdoc/>
    public string? SerialNo => IsConnected ? _serialNo : null;

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return;
        }

        var (handle, deviceInfo) = await Task.Run(() =>
        {
            var opened = IctScanApi.ictScanInitDeviceWithIoContext(IntPtr.Zero);
            if (opened == IntPtr.Zero)
            {
                throw new InvalidOperationException($"打开设备失败，错误码 {IctScanApi.ictScanGetLastErrNo()}。");
            }

            var info = default(IctScanDeviceInfo);
            if (IctScanApi.ictScanGetDeviceInfo(opened, ref info) == 0)
            {
                IctScanApi.ictScanDeinitDevice(opened);
                throw new InvalidOperationException($"读取设备信息失败，错误码 {IctScanApi.ictScanGetLastErrNo()}。");
            }

            return (opened, info);
        }, cancellationToken).ConfigureAwait(false);

        _handle = handle;
        _deviceInfo = deviceInfo;
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        var handle = _handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _handle = IntPtr.Zero;
        await Task.Run(() => IctScanApi.ictScanDeinitDevice(handle), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">
    /// 请求的模态不是指纹。
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="CaptureRequest.Timeout"/> 不为正数。
    /// </exception>
    public async Task<IReadOnlyList<CaptureResult>> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        var handle = _handle;
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("设备未连接。");
        }

        if (request.Modality != BiometricModality.Finger)
        {
            throw new NotSupportedException($"FCK3L 只支持指纹，不支持 {request.Modality}。");
        }

        if (request.Timeout <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Timeout,
                "Timeout 必须为正数：库未定义传 0 的行为，可能无限阻塞。");
        }

        // 采集阻塞到质量达标或超时为止，取消时用 ictScanStopCapture 提前中断。
        using var registration = cancellationToken.Register(() => IctScanApi.ictScanStopCapture(handle));

        var (image, qualityScore) = await Task.Run(
            () => GrabImage(handle, (int)(request.RequestedScore ?? 0), request.Timeout, cancellationToken),
            cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (image is null)
        {
            // 取不到图：起采被拒，或取图本身失败。失败原因见 ictScanGetLastErrNo。
            throw new InvalidOperationException($"采集失败，错误码 {IctScanApi.ictScanGetLastErrNo()}。");
        }

        // 库不提供设备时间，用主机时间；同一次采集的各条结果共用同一时刻。
        var capturedAt = DateTimeOffset.UtcNow;

        // 宽度达到阈值说明是多指图像，交给分割器拆成按指结果。
        if (_deviceInfo.Width >= SlapWidthMin)
        {
            var fingers = _segmenter.Segment(image, _deviceInfo.Width, _deviceInfo.Height);
            if (fingers.Count > 0)
            {
                return fingers.Select(finger => new CaptureResult
                {
                    Modality = BiometricModality.Finger,

                    // 库输出的手指类型字段编码不明，不做映射，宁可置为 Unknown。
                    Position = BiometricPosition.Unknown,
                    Data = finger.Image,
                    Format = CaptureDataFormat.RawImage,

                    // 分割按裁剪处理，分辨率与传感器一致。
                    Image = new CaptureImageInfo(finger.Width, finger.Height, 8, SensorDpi, SensorDpi),
                    QualityScore = finger.QualityScore,
                    CapturedAt = capturedAt,
                }).ToArray();
            }

            // 分割失败：按参考实现回退到整幅图，继续往下走。
        }

        return new[]
        {
            new CaptureResult
            {
                Modality = BiometricModality.Finger,

                // 库不报告实际采集到哪根手指，只能回显请求值。
                Position = request.Position ?? BiometricPosition.Unknown,
                Data = image,
                Format = CaptureDataFormat.RawImage,
                Image = new CaptureImageInfo(_deviceInfo.Width, _deviceInfo.Height, 8, SensorDpi, SensorDpi),
                QualityScore = qualityScore,
                CapturedAt = capturedAt,
            },
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// 本设备没有实时图回调，预览靠<b>拉模型轮询</b>：反复"采一张 + 取最近一帧"，采到就投给
    /// <paramref name="sink"/>，直到被取消。采的就是 <see cref="CaptureAsync"/> 采的那张原图，
    /// 差别只在于<b>不设质量门限</b>（见 <see cref="PreviewQualityGate"/>）、也不做分割 ——
    /// 预览只显示原图，分割是采集结果的事。
    /// </para>
    /// <para>
    /// 每轮之间<b>不做固定节流</b>：设备按自己的物理节奏出帧即可，画面更新得够不够密交给
    /// <see cref="PacingDeviceDecorator"/> 那道下限兜。只有设备调用瞬时返回时才靠
    /// <see cref="PreviewMinInterval"/> 兜住忙等。
    /// </para>
    /// <para>
    /// 某一轮取不到帧<b>不算流失败</b>，跳过接着转，只有连续取不到够久才收手
    /// （见 <see cref="PreviewFailureLimit"/>）。
    /// </para>
    /// </remarks>
    public async Task RunPreviewAsync(PreviewFrameSink sink, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);

        var handle = _handle;
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException("设备未连接。");
        }

        // 取消即请求停止：取图会阻塞到超时，注册一次，让等在途采集里的调用也能被打断。
        using var registration = cancellationToken.Register(() => IctScanApi.ictScanStopCapture(handle));

        try
        {
            // 连续取不到帧的起点，取到帧即归零；null 表示当前没在连续失败中。
            long? failingSince = null;

            while (true)
            {
                var startedAt = Stopwatch.GetTimestamp();

                var (image, _) = await Task.Run(
                    () => GrabImage(handle, PreviewQualityGate, PreviewCaptureTimeout, cancellationToken),
                    cancellationToken).ConfigureAwait(false);

                if (image is null)
                {
                    // 取不到帧**不算失败**：没放手指就取不到，而预览问的正是"现在镜头前有没有手指"。
                    // 一失败就断流的话，操作员几秒不放手预览就没了（参考实现在真机上栽过这一跤）。
                    // 但也不能永远干等：连续取不到够久，就当设备掉线了，收手让上层知道。
                    failingSince ??= Stopwatch.GetTimestamp();
                    if (Stopwatch.GetElapsedTime(failingSince.Value) >= PreviewFailureLimit)
                    {
                        throw new InvalidOperationException(
                            $"连续 {PreviewFailureLimit.TotalSeconds} 秒取不到图像，错误码 {IctScanApi.ictScanGetLastErrNo()}。");
                    }
                }
                else
                {
                    failingSince = null;

                    sink(new PreviewFrame
                    {
                        Data = image,
                        Width = _deviceInfo.Width,
                        Height = _deviceInfo.Height,

                        // 预览取的是整幅原图，无从判断手指落在哪。
                        Position = null,
                        CapturedAt = DateTimeOffset.UtcNow,
                    });
                }

                // 只在整轮异常快时才补一点延迟，不做固定节流（见 PreviewMinInterval）。
                var elapsed = Stopwatch.GetElapsedTime(startedAt);
                if (elapsed < PreviewMinInterval)
                {
                    await Task.Delay(PreviewMinInterval - elapsed, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 取消是预期的停止路径，不是失败：正常返回，不往外抛（见接口约定）。
        }
    }

    /// <summary>
    /// 采一张原图。
    /// </summary>
    /// <param name="handle">
    /// 设备句柄。
    /// </param>
    /// <param name="minQuality">
    /// 质量门限；<c>0</c> 表示不设要求。
    /// </param>
    /// <param name="timeout">
    /// 采集超时（毫秒）。
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌。
    /// </param>
    /// <returns>
    /// 取到图时为其数据与本次达到的质量分；取不到时为 <see langword="null"/> 与 0。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>不抛。</b>取不到图既可能只是没放手指，也可能是硬件故障，而本库用同一个失败返回值表示两者，
    /// 分不开，故"算不算失败"交给调用方定夺 —— 采集算（见 <see cref="CaptureAsync"/>），
    /// 预览不算（见 <see cref="RunPreviewAsync"/>）。
    /// </para>
    /// <para>
    /// 阻塞调用，调用方须放到线程池上。<paramref name="cancellationToken"/> 在这里只用来分辨
    /// "取不到"与"取消触发的停止"；真正打断阻塞的是 <c>ictScanStopCapture</c>，由调用方注册。
    /// </para>
    /// </remarks>
    private (byte[]? Image, int Quality) GrabImage(
        IntPtr handle,
        int minQuality,
        int timeout,
        CancellationToken cancellationToken)
    {
        var quality = 0;
        if (IctScanApi.ictScanStartCaptureImage(handle, minQuality, ref quality, timeout) == 0)
        {
            // 失败可能是取消触发的停止，先判定取消，再当"取不到"。
            cancellationToken.ThrowIfCancellationRequested();
            return (null, 0);
        }

        // 库报出的长度偏小，不足以容纳整幅图，故缓冲区按传感器像素尺寸开；
        // 且不按它报出的长度裁剪 —— RawImage 的数据长度必须等于 Width × Height。
        var buffer = new byte[_deviceInfo.Width * _deviceInfo.Height];
        var length = buffer.Length;
        if (IctScanApi.ictScanGetLastImage(
                handle, buffer, ref length, IctScanApi.ImageFormatRaw, RawImageCompressionRatio) == 0)
        {
            return (null, 0);
        }

        return (buffer, quality);
    }

    /// <summary>
    /// 断开连接并释放设备。可重复调用：未连接时直接返回。
    /// </summary>
    /// <returns>
    /// 表示异步释放操作的任务。
    /// </returns>
    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}