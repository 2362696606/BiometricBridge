using System.Runtime.InteropServices;
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
    // TODO: Make 与 Model 仍是占位值，拿到 MOSIP 的注册信息后替换。
    Make = "EyeIris",
    Model = "EyeIris Platform",
    // 这两项会进设备证书的 Subject(O)：CTK 校验它与签发该证书的 DP 一致，不一致就报
    // "Organization Name is not matching"（2026-10-09 实测 SBI1001 的 Trust Validation）。
    // 值须与 device-provider 目录里那份 DP 证书的组织名相符。
    DeviceProvider = "Shenzhen ICT Global",
    DeviceProviderId = "ICT_CTK_DEVICE_PROVIDER",
    DeviceSubIds = [0],
    // 子类型必须落在模态允许的那几个里：虹膜只接受 Single / Double（Touchless 是指纹专属）。
    // 本设备一次采集可同时拿到左右眼，故取 Double。填错了 CTK 会认不出这个组合，报
    // "The device type of the selected device is not matching the project"。
    DeviceSubType = DeviceSubType.Double,
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

    /// <summary>
    /// 放宽门限那一趟用的质量门限。
    /// </summary>
    /// <remarks>
    /// 0。见 <see cref="DefaultQuality"/> 的备注：本库里 0 是一个有效门限（来者不拒），
    /// 而不是"不设门限"的哨兵值 —— 故它正是"把能拿到的帧拿回来"这一步要的值。
    /// </remarks>
    private const int PermissiveQuality = 0;

    /// <summary>
    /// 放宽门限那一趟的时间上限（秒）。
    /// </summary>
    /// <remarks>
    /// 门限既已放宽到"来者不拒"，库拿到第一帧就会回，这个上限只是兜底。给个短值是为了让总耗时
    /// 有个界：客户端给的 timeout 已经在第一趟用满，再叠一个等长的预算会把最坏情况翻倍。
    /// </remarks>
    private const int PermissiveAttemptTimeoutSeconds = 3;

    /// <summary>
    /// 值得跑兜底那一趟的最小客户端预算（毫秒）。
    /// </summary>
    /// <remarks>
    /// 兜底那一趟要花 <see cref="PermissiveAttemptTimeoutSeconds"/> 秒。客户端给的预算比它还短，
    /// 说明它压根没打算等 —— 那就如实报"采不到"，而不是花掉数倍于预算的时间把它救成成功。
    /// </remarks>
    private const int MinimumTimeoutForFallbackMs = PermissiveAttemptTimeoutSeconds * 1000;

    /// <summary>
    /// 请求停止后，留给在飞回调收尾的宽限。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>这不是"等结束回调"。</b>手动模式下 <c>biospi_cancel</c> 不会投 <c>CB_CAPTURE</c> ——
    /// 那个回调报的是"采集事务自己走完了"（质量达标、或到了 <c>TimeOut</c>），而不限时的手动事务
    /// 永远不会自己走完。头文件对 <c>biospi_cancel</c> 的定义是"<b>停止</b>注册、采集或识别"，
    /// 对 <c>biospi_capture</c> 则写"可以通过 <c>biospi_cancel</c> <b>终止</b>此接口的继续执行"；
    /// 厂商的 C# 与 C++ 两个示例停止时也都只发一句 <c>Cancel</c>，不等任何回调。
    /// </para>
    /// <para>
    /// 故这段宽限只是给已经开始的实时图回调留出收尾时间。早先把"收到 <c>CB_CAPTURE</c>"当成
    /// 流结束的凭据，结果每次停止都要空等满 3 秒再报超时 —— 那个回调根本不会来。
    /// </para>
    /// </remarks>
    private static readonly TimeSpan StopGrace = TimeSpan.FromMilliseconds(300);

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

    /// <summary>
    /// 当前的预览接收端；null 表示没有预览在进行。
    /// </summary>
    /// <remarks>
    /// 蹦床（设备线程）读它，<see cref="RunPreviewAsync"/> 写它，故标记 <see langword="volatile"/>。
    /// </remarks>
    private volatile PreviewFrameSink? _previewSink;

    /// <summary>
    /// 当前预览的"采集已结束"信号；由 <see cref="_captureCallback"/> 完成。
    /// </summary>
    private volatile TaskCompletionSource? _captureEnded;

    /// <summary>
    /// 实时图回调的委托实例。存进字段是硬性要求：只传进原生侧而不持有引用，GC 一旦回收，
    /// 原生侧留下的就是野函数指针，下次设备事件触发即崩（见 <see cref="EyeIrisPlatformApi"/> 的备注）。
    /// </summary>
    private readonly EyeIrisPlatformApi.FireOnLiveImage _liveImageCallback;

    /// <summary>
    /// 采集结果回调的委托实例。存字段的理由同上。
    /// </summary>
    private readonly EyeIrisPlatformApi.FireOnCaptureNotify _captureCallback;

    #endregion

    /// <summary>
    /// 构造设备并准备好原生回调委托。
    /// </summary>
    public EyeIrisDevice()
    {
        // 绑定实例方法的委托：既把 this 保活，原生侧拿到的也是稳定的函数指针，无需 GCHandle。
        _liveImageCallback = OnLiveImage;
        _captureCallback = OnCaptureNotify;
    }

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

            // 回调在连接时注册一次，与厂商的三个示例一致（它们在窗口初始化时就把 7 个回调全注册上）。
            // 不在起预览时注册：biospi_set_callback 是进程级的单个导出，流跑起来后再改注册有竞态。
            // 此刻还没有预览接收端，蹦床会丢掉帧；起预览时才把接收端设上。
            code = EyeIrisPlatformApi.SetLiveImageCallback(
                EyeIrisPlatformApi.CallbackLiveImage, _liveImageCallback, IntPtr.Zero);
            if (code != EyeIrisPlatformApi.NoError)
            {
                EyeIrisPlatformApi.biospi_detach(EyeIrisPlatformApi.NormalDetach);
                throw new InvalidOperationException($"注册实时图回调失败，错误码 0x{code:X8}。");
            }

            // 采集结束回调：预览任务靠它完成，进而释放串行化门，故同样是必需项而非可选项。
            code = EyeIrisPlatformApi.SetCaptureCallback(
                EyeIrisPlatformApi.CallbackCapture, _captureCallback, IntPtr.Zero);
            if (code != EyeIrisPlatformApi.NoError)
            {
                EyeIrisPlatformApi.biospi_detach(EyeIrisPlatformApi.NormalDetach);
                throw new InvalidOperationException($"注册采集回调失败，错误码 0x{code:X8}。");
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
    /// <see cref="CaptureRequest.Positions"/> 含非虹膜位置，或多于一枚。
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="CaptureRequest.Timeout"/> 不为正数，或
    /// <see cref="CaptureRequest.RequestedScore"/> 超出 0~100。
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 设备未连接。
    /// </exception>
    /// <exception cref="BiometricNotDetectedException">
    /// 未能在给定预算内取得有效帧。
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

        var eyes = ResolveEyes(request);

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

        var results = new List<CaptureResult>(2);

        // 最多两趟：第一趟按请求的门限等（达标即回，常规路径的结果与耗时都不变），
        // 一无所获时第二趟放宽门限把帧拿回来 —— requestedScore 是自动采集的触发门限而非拒绝理由，
        // 见 TwoAttempts 的备注。
        //
        // 兜底那一趟要花掉一次完整的采集，故只在客户端给的预算本身就够长时才跑：CTK 把超时用例
        // 分成两批，"无输入"那批只给 500ms（要的是采集类错误），"有输入"那批给 10s（要的才是成功）。
        foreach (var attempt in TwoAttempts(parameters, AllowsFallback(request.Timeout)))
        {
            var (code, leftQuality, rightQuality) = await Task.Run(() =>
            {
                var left = 0;
                var right = 0;
                var captured = EyeIrisPlatformApi.biospi_capture(
                    EyeIrisPlatformApi.CmdTypeSync, attempt, ref left, ref right, leftImage, rightImage);

                // 采集超时<b>不是</b>硬失败：超时也要把库可能已回填的帧拿来看（见下方）。
                // 其余非零码才是真失败（含取消触发的停止 —— 先判定取消再报失败）。
                if (captured != EyeIrisPlatformApi.NoError
                    && captured != EyeIrisPlatformApi.ErrorCaptureTimeout)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new InvalidOperationException(
                        $"采集失败，错误码 0x{captured:X8}（取值见 EyeIrisPlatformApi 的错误码区）。");
                }

                return (captured, left, right);
            }, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            // 库不提供设备时间，用主机时间；同一次采集的各条结果共用同一时刻。
            var capturedAt = DateTimeOffset.UtcNow;

            // 同步模式没有"哪只眼有效"的显式标志 —— 那是异步回调 result 的语义，这里只能自己推。
            // 判据分两种情形：
            //  · 质量分 > 0：达标采到，取走。不采用注册回调解读里的 50 分阈值，那会把
            //    "质量差但仍可用"的帧一并丢掉，与契约相悖。
            //  · 超时且质量分为 0：质量分为 0 只说明"没达到请求的门限"，<b>不代表没有帧</b>，
            //    故再看一眼缓冲里有没有真数据（见 HasImageData）。
            var timedOut = code == EyeIrisPlatformApi.ErrorCaptureTimeout;

            results.Clear();

            if (leftQuality > 0 || (timedOut && HasImageData(leftImage)))
            {
                results.Add(CreateResult(BiometricPosition.LeftIris, leftImage, leftQuality, capturedAt));
            }

            if (rightQuality > 0 || (timedOut && HasImageData(rightImage)))
            {
                results.Add(CreateResult(BiometricPosition.RightIris, rightImage, rightQuality, capturedAt));
            }

            if (results.Count > 0)
            {
                return results;
            }
        }

        // 一趟都没采到：设备本身没问题，是镜头前没有眼睛。宁可显式失败也不返回空集合
        // —— 空集合会被上层当成"没采到"，从而掩盖了这条路径。
        throw new BiometricNotDetectedException("没有采到可用图像：未能在给定预算内取得有效帧。");
    }

    /// <summary>
    /// 把请求的"要采哪只眼"翻成 SDK 的眼睛标志
    /// </summary>
    /// <param name="request">
    /// 采集请求
    /// </param>
    /// <returns>
    /// <see cref="EyeIrisPlatformApi.EyeLeft"/> 等眼睛标志
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <see cref="CaptureRequest.Positions"/> 含非虹膜位置，或多于两枚。
    /// </exception>
    /// <remarks>
    /// <para>
    /// <b>点名的部位优先于分组</b>：点名了就按名采（只点了左眼就采左眼），只有没点名时才由
    /// <see cref="CaptureRequest.Group"/> 决定；两者都没给表示"哪只眼都行"，取
    /// <see cref="EyeIrisPlatformApi.EyeEither"/>。
    /// </para>
    /// <para>
    /// 双眼请求（<c>{LeftIris, RightIris}</c> 或 <c>Group=Both</c>）取
    /// <see cref="EyeIrisPlatformApi.EyeBoth"/> —— 这是"必须两眼"与"随便哪只"的分界，
    /// 厂商示例的注册采集也只用 <c>1/2/3</c> 这三个值。
    /// </para>
    /// <para>
    /// 纯映射，不碰原生：单列出来是为了能脱离真机直接断言。
    /// </para>
    /// </remarks>
    internal static int ResolveEyes(CaptureRequest request)
    {
        var positions = request.Positions;

        if (positions.Any(position => position is not (BiometricPosition.LeftIris or BiometricPosition.RightIris)))
        {
            throw new ArgumentException(
                $"Positions 含非虹膜位置，实际为 [{string.Join(", ", positions)}]。",
                nameof(request));
        }

        if (positions.Count > 2)
        {
            throw new ArgumentException(
                $"Positions 至多两枚（左右眼），实际为 {positions.Count} 枚。",
                nameof(request));
        }

        var left = positions.Contains(BiometricPosition.LeftIris);
        var right = positions.Contains(BiometricPosition.RightIris);

        if (left && right)
        {
            return EyeIrisPlatformApi.EyeBoth;
        }

        if (left)
        {
            return EyeIrisPlatformApi.EyeLeft;
        }

        if (right)
        {
            return EyeIrisPlatformApi.EyeRight;
        }

        return request.Group switch
        {
            CaptureGroup.Left => EyeIrisPlatformApi.EyeLeft,
            CaptureGroup.Right => EyeIrisPlatformApi.EyeRight,
            CaptureGroup.Both => EyeIrisPlatformApi.EyeBoth,
            _ => EyeIrisPlatformApi.EyeEither,
        };
    }

    /// <summary>
    /// 客户端给的预算是否长到值得跑兜底那一趟
    /// </summary>
    /// <param name="requestTimeoutMs">
    /// 请求里的 <c>timeout</c>（毫秒）。
    /// </param>
    /// <returns>
    /// 预算不短于一次兜底采集返回 true。
    /// </returns>
    /// <remarks>
    /// 单列成函数，是因为它正是"有输入超时"与"无输入超时"两批 CTK 用例的分界：前者给 10s，
    /// 要的是成功的最佳帧；后者只给 500ms，要的是采集类错误。判错方向就会把一边的好用例
    /// 变成另一边的坏用例，故它值得能被直接断言。
    /// </remarks>
    internal static bool AllowsFallback(int requestTimeoutMs)
        => requestTimeoutMs >= MinimumTimeoutForFallbackMs;

    /// <summary>
    /// 采集的两趟参数：先按请求的门限，再放宽到"来者不拒"
    /// </summary>
    /// <param name="parameters">
    /// 按请求拼出的第一趟参数。
    /// </param>
    /// <param name="allowFallback">
    /// 是否允许跑兜底那一趟；见 <see cref="MinimumTimeoutForFallbackMs"/>。
    /// </param>
    /// <returns>
    /// 依次给出各趟参数。
    /// </returns>
    /// <remarks>
    /// <para>
    /// <b>规则：<c>requestedScore</c> 是自动采集的触发门限，不是拒绝的理由。</b>规范原文：
    /// "Upon reaching the quality score the biometric device is expected to auto-capture the image.
    /// <i>If the requested score is not met, until the timeout, the best frame during the capture
    /// sequence must be captured/returned.</i>" —— 达不到门限也要把最佳帧<b>当成功</b>交回去。
    /// </para>
    /// <para>
    /// <b>为什么需要两趟才能做到这件事</b>：本库只在这到门限时才回填帧，达不到就空等满 timeout、
    /// 一帧不回（它不提供"窗口内看过哪些帧"）。所以第一趟用请求的门限去等（这一步也顺带把 timeout
    /// 等满），一无所获时第二趟把门限放宽到"来者不拒"，把帧拿回来 —— 客户端此刻仍在位。
    /// </para>
    /// <para>
    /// <b>把门限当硬判据是错的</b>：那样达不到门限就报 101"检测不到生物特征"，而事实是
    /// "生物特征在位、只是没达到请求的质量"。CTK 有三个用例栽在这上面：SBI1050（门限 100，
    /// 顺带还要求响应耗时 <c>&gt;=</c> timeout）、SBI1073 与 SBI1077（门限 60）。
    /// </para>
    /// <para>
    /// <b>反过来，兜底也不能无条件跑</b>：它要花掉一次完整的采集。CTK 另有一批超时用例要的恰恰是
    /// 采集类错误（SBI1040~1043，名字里写着"without any input"，只给 500ms）—— 客户端给这么短的
    /// 预算就是没打算等，花数倍于它的时间去把一次成功救回来是错的。故由
    /// <paramref name="allowFallback"/> 把住：预算短于一次兜底采集时不跑，如实报采不到。
    /// </para>
    /// <para>
    /// 第一趟按请求的门限，故达标即回 —— 常规路径（门限 40 的那些用例）的耗时与结果都和从前一致。
    /// </para>
    /// </remarks>
    internal static IEnumerable<EyeIrisPlatformApi.BioSpiParams> TwoAttempts(
        EyeIrisPlatformApi.BioSpiParams parameters,
        bool allowFallback)
    {
        yield return parameters;

        if (!allowFallback)
        {
            yield break;
        }

        var relaxed = parameters;
        relaxed.Quality = PermissiveQuality;
        relaxed.TimeOut = PermissiveAttemptTimeoutSeconds;

        yield return relaxed;
    }

    /// <summary>
    /// 库回填的图像缓冲里是否有真实数据
    /// </summary>
    /// <param name="image">
    /// 库回填的图像缓冲。
    /// </param>
    /// <returns>
    /// 存在任何一个非零像素返回 true。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 超时时库可能已经回填了最佳帧、却把质量分记 0（质量分报的是"是否达到请求的门限"，
    /// 不是"有没有帧"）。要判"有没有帧"只能另找凭据，而<b>整块缓冲全零</b>是一个真实采集
    /// 不可能出现的情形 —— 任何画面都有底噪，故拿它当"没回填"的判据。
    /// </para>
    /// <para>
    /// 纯函数，单列出来是为了能脱离真机直接断言。
    /// </para>
    /// </remarks>
    internal static bool HasImageData(ReadOnlySpan<byte> image)
        => image.IndexOfAnyExcept((byte)0) >= 0;

    /// <inheritdoc/>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="sink"/> 为 null。
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// 设备未连接、已有预览在进行、或驱动拒绝这次采集，
    /// 或请求停止后迟迟收不到采集结束回调。
    /// </exception>
    /// <exception cref="TimeoutException">
    /// 请求停止后 <see cref="StopGrace"/> 内未收到采集结束回调。
    /// </exception>
    /// <remarks>
    /// <para>
    /// 实现方式是<b>跑一个长时异步采集</b>：本库没有独立的"开流"接口，实时图只在采集进行中
    /// 经 <see cref="EyeIrisPlatformApi.CallbackLiveImage"/> 回调给出。停止即
    /// <see cref="EyeIrisPlatformApi.biospi_cancel"/> —— 头文件管它叫"停止/终止"，
    /// 停完即放手，<b>不等</b> <see cref="EyeIrisPlatformApi.CallbackCapture"/>（见 <see cref="StopGrace"/>）。
    /// </para>
    /// <para>
    /// 采集参数取<b>手动触发</b>：自动模式会在质量达标时结束整次采集，流就断了；手动模式下
    /// 采集不会自行收尾，实时图得以一直流。注意质量分在这里不决定流的存活，
    /// 故取设备默认档位 —— 本库的 0 是"来者不拒"的有效门限（见 <see cref="DefaultQuality"/> 的备注），
    /// 在自动模式下会立刻满足，是不该踩的坑。
    /// </para>
    /// </remarks>
    public async Task RunPreviewAsync(PreviewFrameSink sink, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sink);

        if (!_attached)
        {
            throw new InvalidOperationException("设备未连接。");
        }

        // 单飞：本机的采集是全局单例状态（biospi_cancel 亦然），两台同时流传不出去。
        if (Interlocked.CompareExchange(ref _previewSink, sink, null) is not null)
        {
            throw new InvalidOperationException("该设备已有预览在进行中。");
        }

        var captureEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _captureEnded = captureEnded;

        try
        {
            var parameters = new EyeIrisPlatformApi.BioSpiParams
            {
                Eyes = EyeIrisPlatformApi.EyeAuto,
                Quality = DefaultQuality,
                EyeExpo = EyeExposure,

                // 不限时：流要跑到被停止为止。普通采集禁用 0（见 CaptureAsync 的校验），
                // 这里要的正是它的"不限时"语义。
                TimeOut = 0,
                Type = EyeIrisPlatformApi.CaptureTypeManual,
                Priority = EyeIrisPlatformApi.PrioritySpeed,
                Size = SizeFlag,
            };

            // 取消即请求停流。biospi_cancel 是裸原生调用，不需要设备门 ——
            // 预览正持有设备，此刻不会有别的设备操作在跑。
            using var registration = cancellationToken.Register(static () => EyeIrisPlatformApi.biospi_cancel(0));

            // 异步采集：接受后立即返回，图像与结果都走回调，故出参传局部、图像缓冲传 null。
            var code = await Task.Run(() =>
            {
                var left = 0;
                var right = 0;
                return EyeIrisPlatformApi.biospi_capture(
                    EyeIrisPlatformApi.CmdTypeAsync, parameters, ref left, ref right, null, null);
            }, cancellationToken).ConfigureAwait(false);

            if (code != EyeIrisPlatformApi.NoError)
            {
                // 失败也可能是取消触发的停止，先判定取消再报失败。
                cancellationToken.ThrowIfCancellationRequested();
                throw new InvalidOperationException(
                    $"启动预览失败，错误码 0x{code:X8}（取值见 EyeIrisPlatformApi 的错误码区）。");
            }

            await WaitForStreamEndAsync(captureEnded.Task, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // 先断接收端再清信号：顺序反了的话，蹦床会拿着已经不认的信号继续投帧。
            _previewSink = null;
            _captureEnded = null;
        }
    }

    /// <summary>
    /// 等待预览流结束。
    /// </summary>
    /// <param name="ended">
    /// 由采集结束回调完成的信号。
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌。
    /// </param>
    /// <returns>
    /// 表示等待操作的任务。
    /// </returns>
    /// <remarks>
    /// <para>
    /// 没取消时一直等下去（预览本就该长时运行）。
    /// </para>
    /// <para>
    /// 取消后只再等一个宽限，<b>且等不到回调不算失败</b>：手动模式下那个回调压根不来
    /// （见 <see cref="StopGrace"/>），拿它当结束凭据会让每次停止都变成"超时"。
    /// 已按头文件与厂商两个示例确认 <c>biospi_cancel</c> 是<b>终止</b>采集，宽限到点即可放手。
    /// </para>
    /// </remarks>
    private static async Task WaitForStreamEndAsync(Task ended, CancellationToken cancellationToken)
    {
        var cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellationToken.Register(() => cancelled.TrySetResult());

        if (await Task.WhenAny(ended, cancelled.Task).ConfigureAwait(false) == ended)
        {
            return;
        }

        // 回调来了就早收手，没来就等满宽限 —— 两条路都正常返回。
        await Task.WhenAny(ended, Task.Delay(StopGrace)).ConfigureAwait(false);
    }

    /// <summary>
    /// 实时图回调蹦床。
    /// </summary>
    /// <param name="eye">
    /// 眼别。
    /// </param>
    /// <param name="width">
    /// 图像宽度。
    /// </param>
    /// <param name="height">
    /// 图像高度。
    /// </param>
    /// <param name="liveImage">
    /// 灰度图像数据指针，<b>只在本次回调期间有效</b>。
    /// </param>
    /// <param name="imageSize">
    /// 图像数据字节数。
    /// </param>
    /// <param name="context">
    /// 注册回调时传入的上下文；本类传 <see cref="IntPtr.Zero"/>（委托已绑定实例，不需要它）。
    /// </param>
    /// <remarks>
    /// 由设备线程调用。只做四件事：取接收端、拷图、造帧、投递。
    /// <b>不碰 <see cref="_attached"/> 或任何 <see cref="DisconnectAsync"/> 会改的状态</b>
    /// —— 那些状态在流的存续期内不会变（预览持有设备），碰了反而引入竞态。
    /// </remarks>
    private void OnLiveImage(int eye, int width, int height, IntPtr liveImage, int imageSize, IntPtr context)
    {
        // 没有预览在跑就丢：回调是连接时注册的，本类的同步采集同样会让它触发。
        if (_previewSink is not { } sink || liveImage == IntPtr.Zero || width <= 0 || height <= 0)
        {
            return;
        }

        var expected = width * height;
        if (imageSize < expected)
        {
            return;
        }

        // 指针只在回调期间有效，当场拷走（PreviewFrame.Data 的契约要求调用方拥有缓冲）。
        var data = new byte[expected];
        Marshal.Copy(liveImage, data, 0, expected);

        var frame = new PreviewFrame
        {
            Data = data,
            Width = width,
            Height = height,
            Position = eye switch
            {
                EyeIrisPlatformApi.EyeLeft => BiometricPosition.LeftIris,
                EyeIrisPlatformApi.EyeRight => BiometricPosition.RightIris,
                _ => null,
            },
            CapturedAt = DateTimeOffset.UtcNow,
        };

        try
        {
            sink(frame);
        }
        catch (Exception)
        {
            // 本方法跑在原生回调的栈帧里，异常穿回去会带走进程；接口契约也写明接收端不得抛。
            // 这里兜住并丢弃：最坏是丢一帧。
        }
    }

    /// <summary>
    /// 采集结果回调蹦床：预览流的结束信号来源。
    /// </summary>
    /// <param name="result">
    /// 结果码，见 <see cref="EyeIrisPlatformApi"/> 的采集段常量。
    /// </param>
    /// <param name="leftQuality">
    /// 左眼质量分。
    /// </param>
    /// <param name="rightQuality">
    /// 右眼质量分。
    /// </param>
    /// <param name="leftImage">
    /// 左眼图像指针。
    /// </param>
    /// <param name="rightImage">
    /// 右眼图像指针。
    /// </param>
    /// <param name="context">
    /// 同 <see cref="OnLiveImage"/>。
    /// </param>
    /// <remarks>
    /// 事务<b>自己</b>走完时（质量达标、超时、设备出错）靠它让预览提前收手，不必等到被取消。
    /// 停止预览那条路不依赖它 —— 手动取消不会走这个回调（见 <see cref="StopGrace"/>）。
    /// </remarks>
    private void OnCaptureNotify(int result, uint leftQuality, uint rightQuality,
        IntPtr leftImage, IntPtr rightImage, IntPtr context)
    {
        // 同步采集也会触发本回调；只有预览在跑时才认它，否则会把别人的流提前放行。
        _captureEnded?.TrySetResult();
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
