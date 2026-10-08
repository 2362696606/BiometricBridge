using System.Text;
using BiometricBridge.Core;
using BiometricBridge.Core.Models;
using BiometricBridge.Core.Models.Attributes;
using BiometricBridge.Core.Models.Enums;
using BiometricBridge.Device.Interop;

namespace BiometricBridge.Device;

/// <summary>
/// 虹膜采集设备，经 <c>eye_iris_platform.dll</c> 访问。
/// </summary>
/// <remarks>
/// <para>
/// 只支持虹膜模态。
/// </para>
/// <para>
/// 不做并发保护：设备并发由外层装饰器负责。
/// </para>
/// <para>
/// 原生函数一律阻塞，故全部丢到线程池执行；库对调用线程无要求。
/// </para>
/// <para>
/// 一次采集可能同时拿到左右眼，故一次 <see cref="CaptureAsync"/> 至多返回两条结果。
/// </para>
/// <para>
/// 不在连接时调用 <see cref="EyeIrisPlatformApi.biospi_config"/>：该结构的各字段原生侧都有
/// 默认值，而本抽象层没有承载这些系统级设置的入口。与其在这里写死一组上层无法更改的值，
/// 不如把 SDK 默认值原样交给设备。将来要开放这些参数，应另设配置入口而不是塞进连接流程。
/// </para>
/// </remarks>
[IrisDeviceInfo(
    // TODO: Make 与 Model 是占位值；拿到 MOSIP 的注册信息后连同 DeviceProvider/DeviceProviderId 一并替换。
    Make = "EyeIris",
    Model = "EyeIris Platform",
    DeviceProvider = "FTM-Test",
    DeviceProviderId = "FTM-TEST-001",
    DeviceSubIds = [0],
    DeviceSubType = DeviceSubType.Touchless,
    Certification = CertificationLevel.L0,
    Purpose = Purpose.Registration)]
public class EyeIrisDevice : IBiometricDevice
{
    #region 常量

    /// <summary>
    /// 虹膜图像的像素分辨率（DPI）。
    /// </summary>
    /// <remarks>
    /// 库不提供该值。虹膜图像没有指纹 FAP 那样的 ppi 强制要求，按 ISO/IEC 19794-6
    /// 的常见约定取 200 —— 640×480 恰好对应 3.2×2.4 英寸。
    /// </remarks>
    private const int SensorDpi = 200;

    /// <summary>
    /// 请求未指定质量分时使用的门限。
    /// </summary>
    /// <remarks>
    /// 本设备用途是注册，故取注册档位 <see cref="EyeIrisPlatformApi.ParamsQualityEnroll"/>。
    /// 注意不能像 <see cref="FCK3L"/> 那样退化为 0：该库的 0 是一个有效门限（来者不拒），
    /// 而不是"不设门限"的哨兵值。
    /// </remarks>
    private const int DefaultQuality = EyeIrisPlatformApi.ParamsQualityEnroll;

    /// <summary>
    /// 瞳孔曝光值。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="DefaultQuality"/> 同理，按注册档位取
    /// <see cref="EyeIrisPlatformApi.ParamsEyeExpoEnroll"/>。
    /// </remarks>
    private const int EyeExposure = EyeIrisPlatformApi.ParamsEyeExpoEnroll;

    /// <summary>
    /// 注册采集的区域标志。
    /// </summary>
    /// <remarks>
    /// 头文件标注的默认值：8 位为一组，两组相同表示左右眼用同一设定。库对该字段含义语焉不详，
    /// 故原样沿用默认值，不在此作解释。
    /// </remarks>
    private const int SizeFlag = 0x00000303;

    #endregion

    #region Fileds

    /// <summary>
    /// 是否已连接 SDK 与设备。
    /// </summary>
    private bool _attached;

    /// <summary>
    /// 设备 SN 码，连接成功后填充。
    /// </summary>
    private string? _serialNo;

    #endregion

    /// <inheritdoc/>
    public bool IsConnected => _attached;

    /// <inheritdoc/>
    public string? SerialNo => IsConnected ? _serialNo : null;

    /// <inheritdoc/>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (IsConnected)
        {
            return;
        }

        var serialNo = await Task.Run(() =>
        {
            // attach 方式与厂商自带的三个示例（C++、C# 各一份）保持一致：SLEEP | NET。
            // 用普通的 AttachNormal(0) 实测直接以 BioSPI_DEVICE_ERR_OPEN(102) 失败，
            // 而同一台设备、同一个 eye_iris_platform.dll 在示例里用这组标志能连上。
            var code = EyeIrisPlatformApi.biospi_attach(
                EyeIrisPlatformApi.AttachNormalSleep | EyeIrisPlatformApi.AttachNormalNet,
                IntPtr.Zero,
                null);
            if (code != EyeIrisPlatformApi.NoError)
            {
                // 头文件：连接失败后须先 detach 才能重试。不在此复位，下次 ConnectAsync
                // 会撞上 SDK 的残留状态而必然失败。
                EyeIrisPlatformApi.biospi_detach(EyeIrisPlatformApi.NormalDetach);
                throw new InvalidOperationException($"连接设备失败，错误码 0x{code:X8}。");
            }

            // 结构体含数组字段，须调用前给每个数组字段赋好实例，否则封送失败。
            // 数组字段须与头文件 BioSPI_DEVICE 的 6 个 char[260] + 2 个 int[260] 一一对应，漏一个就会越界。
            var device = new EyeIrisPlatformApi.BioSpiDevice
            {
                VendorCode = new byte[EyeIrisPlatformApi.VersionBufferLength],
                DeviceType = new byte[EyeIrisPlatformApi.VersionBufferLength],
                DeviceNum = new byte[EyeIrisPlatformApi.VersionBufferLength],
                DeviceModel = new byte[EyeIrisPlatformApi.VersionBufferLength],
                ReservedCharInfo1 = new byte[EyeIrisPlatformApi.VersionBufferLength],
                ReservedCharInfo2 = new byte[EyeIrisPlatformApi.VersionBufferLength],
                ReservedIntInfo1 = new int[EyeIrisPlatformApi.VersionBufferLength],
                ReservedIntInfo2 = new int[EyeIrisPlatformApi.VersionBufferLength],
            };

            code = EyeIrisPlatformApi.biospi_device_info(ref device);
            if (code != EyeIrisPlatformApi.NoError)
            {
                // 已 attach 成功，失败路径必须先断开，否则 SDK 保持在已连接状态，
                // 既泄漏资源，也让下一次 attach 失败。
                EyeIrisPlatformApi.biospi_detach(EyeIrisPlatformApi.NormalDetach);
                throw new InvalidOperationException($"读取设备信息失败，错误码 0x{code:X8}。");
            }

            return DecodeAnsiString(device.DeviceNum);
        }, cancellationToken).ConfigureAwait(false);

        // 设备未上报 SN 时留 null —— SN 是设备自己声明的，编不出一个真的来。
        _serialNo = serialNo;
        _attached = true;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// <b>原生 detach 不受 <paramref name="cancellationToken"/> 约束。</b>这与 <see cref="FCK3L"/>
    /// 的写法不同，是有意为之：本方法一进来就把 <see cref="_attached"/> 置为 <see langword="false"/>，
    /// 只要 detach 中途被取消，对象就会声称"已断开"而 SDK 仍处于已连接状态；加之头文件要求
    /// 退出前必须 detach，故这次调用必须发生。detach 本身不阻塞，取消它也无收益。
    /// </remarks>
    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (!_attached)
        {
            return;
        }

        _attached = false;

        // 返回值忽略：头文件未定义 detach 失败的语义，且此刻已无补救动作可做。
        await Task.Run(() => EyeIrisPlatformApi.biospi_detach(EyeIrisPlatformApi.NormalDetach))
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">
    /// 请求的模态不是虹膜。
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <see cref="CaptureRequest.Position"/> 不是虹膜位置。
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="CaptureRequest.Timeout"/> 不为正数，或
    /// <see cref="CaptureRequest.RequestedScore"/> 超出 0~100。
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 设备未连接，或采集成功但两只眼都没拿到可用图像。
    /// </exception>
    public async Task<IReadOnlyList<CaptureResult>> CaptureAsync(
        CaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_attached)
        {
            throw new InvalidOperationException("设备未连接。");
        }

        if (request.Modality != BiometricModality.Iris)
        {
            throw new NotSupportedException($"EyeIrisDevice 只支持虹膜，不支持 {request.Modality}。");
        }

        if (request.Timeout <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.Timeout,
                "Timeout 必须为正数：库未定义传 0 的行为，可能无限阻塞。");
        }

        // null 与 Unknown 都表示"不指定眼睛"，交给设备自选。
        var eyes = request.Position switch
        {
            null or BiometricPosition.Unknown => EyeIrisPlatformApi.EyeAuto,
            BiometricPosition.LeftIris => EyeIrisPlatformApi.EyeLeft,
            BiometricPosition.RightIris => EyeIrisPlatformApi.EyeRight,
            _ => throw new ArgumentException(
                $"Position 必须是 LeftIris、RightIris、Unknown 或 null，实际为 {request.Position}。",
                nameof(request)),
        };

        var quality = (int)(request.RequestedScore ?? DefaultQuality);
        if (quality is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                request.RequestedScore,
                "RequestedScore 必须在 0~100：库的质量分沿用 0~100 标度（见其 ParamsQuality* 常量）。");
        }

        // 接口的 Timeout 是毫秒，库要的是秒。向上取整并至少 1 秒，
        // 免得把不足一秒的正数截成 0，落到库的"不限时"语义上。
        var timeoutSeconds = Math.Max(1, (int)Math.Ceiling(request.Timeout / 1000.0));

        var parameters = new EyeIrisPlatformApi.BioSpiParams
        {
            Eyes = eyes,
            Quality = quality,
            EyeExpo = EyeExposure,
            TimeOut = timeoutSeconds,
            Type = EyeIrisPlatformApi.CaptureTypeAuto,

            // 自动触发方式下库会忽略优先级，取默认档即可。
            Priority = EyeIrisPlatformApi.PrioritySpeed,
            Size = SizeFlag,
        };

        var leftImage = new byte[EyeIrisPlatformApi.IrisImageSize];
        var rightImage = new byte[EyeIrisPlatformApi.IrisImageSize];

        // 采集阻塞到质量达标或超时为止，取消时用 biospi_cancel 提前中断。
        using var registration = cancellationToken.Register(() => EyeIrisPlatformApi.biospi_cancel(0));

        var (leftQuality, rightQuality) = await Task.Run(() =>
        {
            var left = 0;
            var right = 0;
            var code = EyeIrisPlatformApi.biospi_capture(
                EyeIrisPlatformApi.CmdTypeSync, parameters, ref left, ref right, leftImage, rightImage);
            if (code != EyeIrisPlatformApi.NoError)
            {
                // 失败可能是取消触发的停止，先判定取消再报失败。
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException(
                    $"采集失败，错误码 0x{code:X8}（取值见 EyeIrisPlatformApi 的错误码区）。");
            }

            return (left, right);
        }, cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        // 库不提供设备时间，用主机时间；同一次采集的各条结果共用同一时刻。
        var capturedAt = DateTimeOffset.UtcNow;

        // 同步模式没有"哪只眼有效"的显式标志 —— 那是异步回调 result 的语义，
        // 这里只能按质量分推断：没采到的那只眼，库给的质量分是 0。
        // 不采用注册回调解读里的 50 分阈值，那会把"质量差但仍可用"的帧一并丢掉，
        // 与"超时返回本次采集中质量最佳的帧"的契约相悖。
        var results = new List<CaptureResult>(2);
        if (leftQuality > 0)
        {
            results.Add(CreateResult(BiometricPosition.LeftIris, leftImage, leftQuality, capturedAt));
        }

        if (rightQuality > 0)
        {
            results.Add(CreateResult(BiometricPosition.RightIris, rightImage, rightQuality, capturedAt));
        }

        if (results.Count == 0)
        {
            // 库报成功却两只眼质量分都是 0，属自相矛盾，宁可显式失败也不返回空集合
            // ——空集合会被上层当成"没采到"，从而掩盖了这条异常路径。
            throw new InvalidOperationException("采集返回成功，但左右眼质量分均为 0，没有可用图像。");
        }

        return results;
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

    /// <summary>
    /// 按眼别拼装一条采集结果。
    /// </summary>
    /// <param name="position">
    /// 眼别。
    /// </param>
    /// <param name="image">
    /// 该眼的灰度图数据。
    /// </param>
    /// <param name="qualityScore">
    /// 该眼的质量分，直接取库的原始分值，不做归一化。
    /// </param>
    /// <param name="capturedAt">
    /// 采集时刻。
    /// </param>
    /// <returns>
    /// 采集结果。
    /// </returns>
    private static CaptureResult CreateResult(
        BiometricPosition position, byte[] image, int qualityScore, DateTimeOffset capturedAt)
    {
        return new CaptureResult
        {
            Modality = BiometricModality.Iris,
            Position = position,
            Data = image,
            Format = CaptureDataFormat.RawImage,
            Image = new CaptureImageInfo(
                EyeIrisPlatformApi.IrisImageWidth,
                EyeIrisPlatformApi.IrisImageHeight,
                8,
                SensorDpi,
                SensorDpi),
            QualityScore = qualityScore,
            CapturedAt = capturedAt,
        };
    }

    /// <summary>
    /// 把原生结构里定长的 ANSI 字节数组解成字符串。
    /// </summary>
    /// <param name="buffer">
    /// 定长缓冲区，以 NUL 结尾；也可能整个都是 NUL。
    /// </param>
    /// <returns>
    /// 截到首个 NUL 为止的字符串；整个缓冲区都是 NUL 时返回 null。
    /// </returns>
    /// <remarks>
    /// 编码取 ASCII。厂商未说明这些字段是否可能是 UTF-8，设备 SN 按惯例是 ASCII；
    /// 若实测读到乱码，先怀疑这里（<see cref="EyeIrisPlatformApi.BioSpiDevice"/> 的备注有同样提示）。
    /// </remarks>
    private static string? DecodeAnsiString(byte[] buffer)
    {
        var end = Array.IndexOf(buffer, (byte)0);
        if (end < 0)
        {
            end = buffer.Length;
        }

        return end == 0 ? null : Encoding.ASCII.GetString(buffer, 0, end);
    }
}
