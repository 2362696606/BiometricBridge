using System.Globalization;
using BiometricBridge.Core;
using BiometricBridge.Host.Common;
using BiometricBridge.Host.Config;
using BiometricBridge.Host.Crypto;
using BiometricBridge.Host.Dto;
using BiometricBridge.Host.Dto.Enum;
using BiometricBridge.Host.Iso;
using Microsoft.IdentityModel.Tokens;
using CoreEnums = BiometricBridge.Core.Models.Enums;
using CoreModels = BiometricBridge.Core.Models;

namespace BiometricBridge.Host;

/// <summary>
/// SBI 注册采集：把 <c>RCAPTURE</c> 请求落到设备上，再按规范拼出响应报文
/// </summary>
/// <remarks>
/// <para>
/// 不引任何 HTTP 依赖 —— 端口、路由、响应头都是宿主的事，这里只按规范采数据、拼响应体。
/// </para>
/// <para>
/// <b>只做注册侧</b>：认证采集（<c>CAPTURE</c>）会额外做会话密钥加密，另一条路径，本类不涉及。
/// </para>
/// <para>
/// <b>生物特征数据的编码</b>：设备只给原始灰度图，<c>bioValue</c> 所需的 ISO 19794 记录在本层
/// 现场编码（见 <see cref="Iso19794FingerImage"/>、<see cref="Iso19794IrisImage"/>）。
/// </para>
/// <para>
/// <b>成败都走 HTTP 200</b>：失败是协议里的一种正常回应，由条目里的错误对象表达，不是异常
/// —— 见 <see cref="SbiError"/>。故本方法不因"采不到"而抛。
/// </para>
/// </remarks>
public sealed class RegistrationCaptureService
{
    #region 常量

    /// <summary>
    /// 请求未给超时时用的默认值（毫秒）
    /// </summary>
    /// <remarks>
    /// 设备侧采集是阻塞式的，必须给它一个有界的超时；选一个足够宽但不至于把请求吊死的值。
    /// </remarks>
    private const int DefaultTimeoutMs = 30000;

    #endregion

    #region Fileds

    /// <summary>
    /// 设备清单
    /// </summary>
    private readonly IDeviceInventory _inventory;

    /// <summary>
    /// 设备采集
    /// </summary>
    private readonly IDeviceCapture _capture;

    /// <summary>
    /// 签名器提供者
    /// </summary>
    private readonly DeviceSignerProvider _signers;

    /// <summary>
    /// 虹膜图像的 JPEG2000 编码器
    /// </summary>
    /// <remarks>
    /// 实现带平台原生库，故由组合根注入（见 <see cref="IJpeg2000Encoder"/> 的备注）；本层只认抽象。
    /// </remarks>
    private readonly IJpeg2000Encoder _jpeg2000;

    /// <summary>
    /// 报文里 <c>env</c> 的默认取值
    /// </summary>
    private readonly DeviceEnvironment _defaultEnv;

    #endregion

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="inventory">
    /// 设备清单
    /// </param>
    /// <param name="capture">
    /// 设备采集
    /// </param>
    /// <param name="signers">
    /// 签名器提供者
    /// </param>
    /// <param name="jpeg2000">
    /// 虹膜图像的 JPEG2000 编码器
    /// </param>
    /// <param name="options">
    /// MOSIP 侧设置（取其中的 <c>env</c>）
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// 任一参数为 null
    /// </exception>
    public RegistrationCaptureService(
        IDeviceInventory inventory,
        IDeviceCapture capture,
        DeviceSignerProvider signers,
        IJpeg2000Encoder jpeg2000,
        MosipOptions options)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(signers);
        ArgumentNullException.ThrowIfNull(jpeg2000);
        ArgumentNullException.ThrowIfNull(options);

        _inventory = inventory;
        _capture = capture;
        _signers = signers;
        _jpeg2000 = jpeg2000;

        // 配置写错时退回规范允许的默认环境，不让一个笔误把端点整个打挂（与 /info 同处理）。
        _defaultEnv = Enum.TryParse<DeviceEnvironment>(options.Env, ignoreCase: true, out var env)
            ? env
            : DeviceEnvironment.Staging;
    }

    /// <summary>
    /// 执行一次注册采集
    /// </summary>
    /// <param name="request">
    /// 采集请求
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌
    /// </param>
    /// <returns>
    /// 采集响应；失败时是单元素、其错误对象有值的响应
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="request"/> 为 null
    /// </exception>
    public async Task<RegistrationCaptureResponse> CaptureAsync(
        RegistrationCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Bio is not { Length: > 0 } requirements)
        {
            // 没有要采的模态：拼不出任何条目，协议为"失败"定的正是单元素错误响应。
            return Failure(SbiErrorHelper.Custom(504, "Invalid capture request: bio is empty"));
        }

        // 事务号要合规范给的长度与字符集，不合就不该碰设备 —— CTK 的 SBI1110 钉的正是这个码与描述。
        if (!IsValidTransactionId(request.TransactionId))
        {
            return Failure(SbiErrorHelper.InvalidTransactionId);
        }

        var snapshots = _inventory.GetDeviceSnapshots();
        var entries = new List<RegistrationCaptureResponseBiometric>();

        // 各块的哈希链跨所有模态串接；客户端首块给的 previousHash 优先，否则用空串的哈希。
        string? chainHash = null;

        foreach (var requirement in requirements)
        {
            if (!TryGetModality(requirement.Type, out var modality))
            {
                entries.Add(ErrorEntry(SbiErrorHelper.Custom(504, "Invalid biometric type")));
                continue;
            }

            if (ResolveDevice(snapshots, requirement.DeviceId, modality) is not { } device)
            {
                entries.Add(ErrorEntry(SbiErrorHelper.DeviceNotFound));
                continue;
            }

            var previousHash = requirement.PreviousHash is { Length: > 0 } given
                ? given
                : chainHash ?? SbiHashChain.EmptyPreviousHash;

            var captured = await CaptureOneAsync(device, requirement, modality, request, previousHash, cancellationToken)
                .ConfigureAwait(false);

            foreach (var entry in captured.Entries)
            {
                entries.Add(entry);
            }

            if (captured.LastHash is { } lastHash)
            {
                chainHash = lastHash;
            }

            if (captured.Error is { } error)
            {
                // 这台设备采不出来：一个模态的错误块即可，不再往下走别的模态。
                entries.Add(ErrorEntry(error));
                break;
            }
        }

        return new RegistrationCaptureResponse { Biometrics = [.. entries] };
    }

    /// <summary>
    /// 在一次要求上执行采集，并拼出对应的响应条目
    /// </summary>
    /// <param name="device">
    /// 目标设备
    /// </param>
    /// <param name="requirement">
    /// 本次要采的模态要求
    /// </param>
    /// <param name="modality">
    /// 设备抽象层的模态
    /// </param>
    /// <param name="request">
    /// 整个采集请求（取共用字段）
    /// </param>
    /// <param name="previousHash">
    /// 首个数据块的 <c>previousHash</c>
    /// </param>
    /// <param name="cancellationToken">
    /// 取消令牌
    /// </param>
    /// <returns>
    /// 条目、末块哈希，或错误对象
    /// </returns>
    private async Task<(List<RegistrationCaptureResponseBiometric> Entries, string? LastHash, SbiError? Error)>
        CaptureOneAsync(
            CoreModels.DeviceSnapshot device,
            RegistrationCaptureRequestBio requirement,
            CoreEnums.BiometricModality modality,
            RegistrationCaptureRequest request,
            string previousHash,
            CancellationToken cancellationToken)
    {
        var deviceInfo = device.Info!;

        // 签名与身份都要序列号，而它只有设备连上后才报得出来 —— 取不到就没法拼出可验签的条目。
        var serialNo = deviceInfo.SerialNo;
        if (string.IsNullOrEmpty(serialNo))
        {
            return ([], null, SbiErrorHelper.DeviceNotReady);
        }

        // 把报文要求翻成设备请求，并做规范要求的一致性校验；不自洽的请求根本不碰设备。
        if (!TryBuildRequest(
                requirement,
                modality,
                deviceInfo.DeviceSubIds,
                request.Timeout ?? DefaultTimeoutMs,
                out var captureRequest,
                out var requested,
                out var buildError))
        {
            return ([], null, buildError);
        }

        IReadOnlyList<CoreModels.CaptureResult> results;
        try
        {
            results = await _capture.CaptureAsync(device.DeviceId, captureRequest, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (KeyNotFoundException)
        {
            return ([], null, SbiErrorHelper.DeviceNotFound);
        }
        catch (DeviceBusyException)
        {
            // 该设备已有采集在跑：规范要求当场报忙。
            return ([], null, SbiErrorHelper.DeviceBusy);
        }
        catch (BiometricNotDetectedException)
        {
            // 设备好着，只是这一趟没采到东西（超时无一帧可用，或质量分全为 0）。协议里这是 101
            // "检测不到生物特征"，不是 110 —— 报成 110 会让操作员去查设备，而该看的是自己的姿势。
            return ([], null, SbiErrorHelper.UnableToDetectBiometricObject);
        }
        catch (InvalidOperationException)
        {
            // 设备未连接（或未就绪）、以及驱动层面的硬失败都落这里：设备侧对这两种都抛它。
            return ([], null, SbiErrorHelper.DeviceNotReady);
        }
        catch (Exception)
        {
            return ([], null, SbiErrorHelper.TechnicalErrorDuringExtraction);
        }

        if (results.Count == 0)
        {
            return ([], null, SbiErrorHelper.UnableToDetectBiometricObject);
        }

        IJwsSigner signer;
        string signedDigitalId;
        try
        {
            signer = _signers.GetSigner(serialNo, deviceInfo.Make, deviceInfo.Model, deviceInfo.DeviceProvider);
            signedDigitalId = signer.SignDigitalId(SbiDigitalIdFactory.Create(deviceInfo, serialNo));
        }
        catch (Exception)
        {
            // 证书签发失败（DP 材料缺失等）：与设备无关的 500 级问题，记在错误条目里。
            return ([], null, SbiErrorHelper.TechnicalErrorDuringExtraction);
        }

        // 要出条目的槽位：点了部位的按请求逐枚对齐，未点的就把采到的都出。null 槽位表示
        // "豁免且未采到"，回填一个错误条目。
        List<CoreModels.CaptureResult?> slots;
        if (requested.Count > 0)
        {
            var excepted = MapPositions(requirement.Exception, modality);
            slots = new List<CoreModels.CaptureResult?>(requested.Count);
            foreach (var requestedPosition in requested)
            {
                var match = results.FirstOrDefault(result => result.Position == requestedPosition);
                if (match is null && !excepted.Contains(requestedPosition))
                {
                    // 请求的部位没采到、又没被豁免：这是真正的失败。
                    return ([], null, SbiErrorHelper.UnableToDetectBiometricObject);
                }

                slots.Add(match);
            }
        }
        else
        {
            slots = [.. results];
        }

        var entries = new List<RegistrationCaptureResponseBiometric>(slots.Count);
        var runningHash = previousHash;

        foreach (var slot in slots)
        {
            // 豁免而未采到的那一枚：回填一个错误条目，使条目数与请求相符，但不让整次失败。
            if (slot is null)
            {
                entries.Add(ErrorEntry(SbiErrorHelper.UnableToDetectBiometricObject));
                continue;
            }

            if (!TryEncode(slot, out var isoRecord))
            {
                return (entries, runningHash, SbiErrorHelper.Custom(504, "Unsupported biometric data"));
            }

            var hash = SbiHashChain.Next(runningHash, isoRecord);
            runningHash = hash;

            var payload = CreatePayload(
                request, deviceInfo, serialNo, slot, modality, isoRecord, requirement.RequestedScore,
                signedDigitalId);

            entries.Add(new RegistrationCaptureResponseBiometric
            {
                SpecVersion = SbiSpec.Version,
                Data = signer.SignRegistrationCaptureData(payload),
                Hash = hash,
                Error = SbiErrorHelper.Success,
            });
        }

        return (entries, runningHash, null);
    }

    /// <summary>
    /// 把一组子类型字面量映射成采集位置（去重、保序）
    /// </summary>
    /// <param name="bioSubTypes">
    /// 子类型字面量数组（如 <c>bioSubType</c> 或 <c>exception</c>）
    /// </param>
    /// <param name="modality">
    /// 生物特征模态
    /// </param>
    /// <returns>
    /// 解析出的位置；取不到或为 <c>UNKNOWN</c> 的项被略过
    /// </returns>
    private static List<CoreEnums.BiometricPosition> MapPositions(
        string[]? bioSubTypes,
        CoreEnums.BiometricModality modality)
    {
        var positions = new List<CoreEnums.BiometricPosition>();
        if (bioSubTypes is null)
        {
            return positions;
        }

        foreach (var bioSubType in bioSubTypes)
        {
            if (SbiMapping.TryMapPosition(bioSubType, modality, out var position)
                && !positions.Contains(position))
            {
                positions.Add(position);
            }
        }

        return positions;
    }

    /// <summary>
    /// 拼一条采集数据载荷
    /// </summary>
    /// <param name="request">
    /// 采集请求
    /// </param>
    /// <param name="deviceInfo">
    /// 设备静态信息
    /// </param>
    /// <param name="serialNo">
    /// 设备序列号
    /// </param>
    /// <param name="result">
    /// 单条采集结果
    /// </param>
    /// <param name="modality">
    /// 设备抽象层的模态
    /// </param>
    /// <param name="isoRecord">
    /// 现场编码出的 ISO 19794 记录
    /// </param>
    /// <param name="requestedScore">
    /// 请求里的最低分数（字符串），原样转给 <see cref="SbiScore.FromRequested"/>
    /// </param>
    /// <param name="signedDigitalId">
    /// 已签名的数字标识 JWS
    /// </param>
    /// <returns>
    /// 采集数据载荷
    /// </returns>
    private RegistrationCaptureData CreatePayload(
        RegistrationCaptureRequest request,
        CoreModels.DeviceInfo deviceInfo,
        string serialNo,
        CoreModels.CaptureResult result,
        CoreEnums.BiometricModality modality,
        byte[] isoRecord,
        string? requestedScore,
        string signedDigitalId)
    {
        return new RegistrationCaptureData
        {
            DigitalId = signedDigitalId,
            BioType = SbiMapping.MapBiometricType(modality),
            DeviceCode = serialNo,
            DeviceServiceVersion = SbiSpec.Version,
            BioSubType = SbiMapping.MapBioSubType(modality, result.Position),
            Purpose = request.Purpose ?? Purpose.Registration,
            Env = request.Env ?? _defaultEnv,
            BioValue = Base64UrlEncoder.Encode(isoRecord),
            TransactionId = request.TransactionId ?? string.Empty,
            Timestamp = FormatTimestamp(result.CapturedAt),
            RequestedScore = SbiScore.FromRequested(requestedScore),
            QualityScore = SbiScore.FromScore(result.QualityScore),
        };
    }

    /// <summary>
    /// 把单条采集结果编码成 ISO 19794 记录
    /// </summary>
    /// <param name="result">
    /// 采集结果
    /// </param>
    /// <param name="isoRecord">
    /// 编码出的记录
    /// </param>
    /// <returns>
    /// 编码成功返回 true；模态无编码器、或数据形态不支持（非原始图、缺几何信息）返回 false
    /// </returns>
    /// <remarks>
    /// 不是静态：虹膜那一路要经过注入的 <see cref="IJpeg2000Encoder"/> 做一次压缩。
    /// </remarks>
    private bool TryEncode(CoreModels.CaptureResult result, out byte[] isoRecord)
    {
        isoRecord = null!;

        try
        {
            switch (result.Modality)
            {
                case CoreEnums.BiometricModality.Finger:
                    isoRecord = Iso19794FingerImage.Encode(result);
                    return true;

                case CoreEnums.BiometricModality.Iris:
                    // 虹膜记录里的图像数据必须是真正的 JPEG2000 码流（见 Iso19794IrisImage 的备注），
                    // 而设备只给原始灰度图，故在这里补上这一步。几何信息随结果一起交给编码器，
                    // 让它自己核对"像素数与宽高是否同源"。
                    if (result.Image is not { } image)
                    {
                        return false;
                    }

                    isoRecord = Iso19794IrisImage.Encode(
                        result,
                        _jpeg2000.Encode(result.Data, image.Width, image.Height));

                    return true;

                default:
                    return false;
            }
        }
        catch (ArgumentException)
        {
            // 数据形态不支持（非原始图、几何信息缺失、位深不对……）：按"编不了"交给上层报错。
            return false;
        }
        catch (Jpeg2000EncodingException)
        {
            // 编码器自己失败。不静默降级为有损格式（见其备注），同样如实报错。
            return false;
        }
    }

    /// <summary>
    /// 按请求里的设备 id 与模态选出一台设备
    /// </summary>
    /// <param name="snapshots">
    /// 设备快照
    /// </param>
    /// <param name="deviceId">
    /// 请求里的设备 id；为空表示不限
    /// </param>
    /// <param name="modality">
    /// 要采的模态
    /// </param>
    /// <returns>
    /// 选中的设备；选不出时返回 null
    /// </returns>
    /// <remarks>
    /// 给了设备 id 就按它精确匹配（发现端点报出的就是 <see cref="CoreModels.DeviceSnapshot.DeviceId"/> 的字符串形态）；
    /// 没给则退到"唯一那台支持该模态的设备"，多台时宁可不选 —— 采错设备的代价比报错大。
    /// </remarks>
    private static CoreModels.DeviceSnapshot? ResolveDevice(
        IReadOnlyList<CoreModels.DeviceSnapshot> snapshots,
        string? deviceId,
        CoreEnums.BiometricModality modality)
    {
        var usable = snapshots.Where(snapshot => snapshot.Info is not null).ToArray();

        if (!string.IsNullOrEmpty(deviceId))
        {
            return usable.FirstOrDefault(snapshot =>
                string.Equals(snapshot.DeviceId.ToString(), deviceId, StringComparison.OrdinalIgnoreCase));
        }

        var matching = usable.Where(snapshot => snapshot.Info!.Modality == modality).ToArray();
        return matching.Length == 1 ? matching[0] : null;
    }

    /// <summary>
    /// 请求里的类型映射成设备抽象层的模态
    /// </summary>
    /// <param name="type">
    /// 请求里的类型
    /// </param>
    /// <param name="modality">
    /// 解析出的模态
    /// </param>
    /// <returns>
    /// 取值合法返回 true
    /// </returns>
    private static bool TryGetModality(BiometricType? type, out CoreEnums.BiometricModality modality)
    {
        switch (type)
        {
            case BiometricType.Finger:
                modality = CoreEnums.BiometricModality.Finger;
                return true;
            case BiometricType.Iris:
                modality = CoreEnums.BiometricModality.Iris;
                return true;
            case BiometricType.Face:
                modality = CoreEnums.BiometricModality.Face;
                return true;
            default:
                modality = default;
                return false;
        }
    }

    /// <summary>
    /// 把报文的 <c>bio[]</c> 元素翻成设备层的采集请求，并做规范要求的一致性校验
    /// </summary>
    /// <param name="requirement">
    /// 本次采集要求（报文里的一个 <c>bio[]</c> 元素）
    /// </param>
    /// <param name="modality">
    /// 设备抽象层的模态
    /// </param>
    /// <param name="deviceSubIds">
    /// 设备广播的子 ID 列表
    /// </param>
    /// <param name="timeoutMs">
    /// 本次采集的超时（毫秒）
    /// </param>
    /// <param name="request">
    /// 拼出的设备请求
    /// </param>
    /// <param name="requested">
    /// 请求里点名的部位（保序去重），供后续逐个槽位配对
    /// </param>
    /// <param name="error">
    /// 不自洽时的错误对象；自洽时为 null
    /// </param>
    /// <returns>
    /// 自洽返回 true
    /// </returns>
    /// <remarks>
    /// <para>
    /// 校验放在<b>调用设备之前</b>：只有这一层同时握有协议字面量与设备能力。不自洽的请求不该
    /// 落到设备上 —— 白采一趟再报错，等于把请求方的笔误变成一次真机操作。
    /// </para>
    /// <para>
    /// 错误码取 <see cref="SbiErrorHelper.RequestedBiometricCountNotSupported"/>（<c>109</c>）：
    /// CTK 的数量类响应 schema（<c>BioCountMismatchRCaptureResponseSchema.json</c>）把它钉死成
    /// <c>109</c> + "Requested number of biometric (Finger/IRIS) not supported"，本服务那一对取值
    /// 逐字相同。分组与子类型不符这类问题规范没给专用码，也归它 —— 语义（请求的生物特征规格不受支持）
    /// 是现有码里最近的。
    /// </para>
    /// <para>
    /// 纯计算（除 <see cref="ParseScore"/> 外无依赖），单列出来是为了能脱离真机直接断言。
    /// </para>
    /// </remarks>
    internal static bool TryBuildRequest(
        RegistrationCaptureRequestBio requirement,
        CoreEnums.BiometricModality modality,
        IReadOnlyList<int> deviceSubIds,
        int timeoutMs,
        out CoreModels.CaptureRequest request,
        out IReadOnlyList<CoreEnums.BiometricPosition> requested,
        out SbiError? error)
    {
        request = null!;
        requested = [];
        error = null;

        // 1) deviceSubId → 分组，并核对设备确实有这个子模块。
        var group = CoreEnums.CaptureGroup.Any;
        if (requirement.DeviceSubId is { } deviceSubId)
        {
            if (!SbiBioSubTypes.TryGroupOfDeviceSubId(deviceSubId, out group))
            {
                error = SbiErrorHelper.RequestedBiometricCountNotSupported;
                return false;
            }

            // 只广播 0 的设备按"无子模块限制"处理（虹膜就是这种）：0 表示"不知道用哪个子模块"。
            if (deviceSubIds.Count > 0
                && !deviceSubIds.Contains(0)
                && !deviceSubIds.Contains(SbiBioSubTypes.DeviceSubIdOf(group)))
            {
                error = SbiErrorHelper.RequestedBiometricCountNotSupported;
                return false;
            }
        }

        // 2) bioSubType → 具名部位。UNKNOWN 与空项是合法的"不点名"，跳过即可；
        //    其余认不出来的字面量是请求方的笔误，不宽容（宽容只会静默采了别的部位）。
        var literals = requirement.BioSubType ?? [];
        var named = new List<CoreEnums.BiometricPosition>(literals.Length);
        foreach (var literal in literals)
        {
            if (string.IsNullOrEmpty(literal) || SbiBioSubTypes.IsUnknownLiteral(literal))
            {
                continue;
            }

            if (!SbiBioSubTypes.TryParseLiteral(literal, modality, out var position))
            {
                error = SbiErrorHelper.RequestedBiometricCountNotSupported;
                return false;
            }

            if (!named.Contains(position))
            {
                named.Add(position);
            }
        }

        // 3) 点名的部位必须落在 deviceSubId 指的那一组里 —— 规范："SBI must detect if the biometric
        //    placement doesn't match the deviceSubId"。只广播"不指定"时无从对账，跳过。
        if (group != CoreEnums.CaptureGroup.Any
            && !named.All(position => SbiBioSubTypes.IsInGroup(position, modality, group)))
        {
            error = SbiErrorHelper.RequestedBiometricCountNotSupported;
            return false;
        }

        // 4) count 由 bioSubType 的个数驱动（规范原话）。显式给了就必须对得上，显式给 0 也一样 ——
        //    "要零枚"是个不受支持的数量（没有生物特征可交），CTK 的数量类用例钉的正是 109。
        //    与"没给 count"区分开：后者表示不指定，由子类型个数或设备定。
        int count;
        if (requirement.Count is not { } declaredCount)
        {
            count = literals.Length;
        }
        else if (declaredCount == 0 || (literals.Length > 0 && declaredCount != literals.Length))
        {
            error = SbiErrorHelper.RequestedBiometricCountNotSupported;
            return false;
        }
        else
        {
            count = declaredCount;
        }

        // 5) 虹膜的"任意两枚"就是双眼：没点名却要两枚时，把分组升到"双"，
        //    否则设备只会按"任意一只眼"去采（虹膜一共就两只，"任意两枚"没有别的解释）。
        if (named.Count == 0
            && modality == CoreEnums.BiometricModality.Iris
            && group == CoreEnums.CaptureGroup.Any
            && count == 2)
        {
            group = CoreEnums.CaptureGroup.Both;
        }

        // 6) count 不得超过该分组实际能给到的枚数。规范两处都指向这里：bio.count 要
        //    "in line with the type of biometric that's captured"，且 SBI 要能发现
        //    "the biometric placement doesn't match the deviceSubId"。
        //    第 4 步只在点名了部位时才对账，没点名时（bioSubType 为空）只能由这一步兜住 ——
        //    CTK 的数量类用例 SBI1036/1037/1038 正是这个形状：deviceSubId 指左/右/双，却要 2/2/3 枚。
        //    各组能给几枚就是 SbiBioSubTypes 里那张成员表；人脸在表里是空集（规范不使用 bioSubType），
        //    按规范给它定值 —— 一次只有一枚。
        var capacity = modality == CoreEnums.BiometricModality.Face
            ? 1
            : SbiBioSubTypes.PositionsOfGroup(modality, group).Count;

        if (count > capacity)
        {
            error = SbiErrorHelper.RequestedBiometricCountNotSupported;
            return false;
        }

        // 7) 请求方给的 previousHash 会被十六进制解码，形状不对就不是"不自洽"而是根本解不了 ——
        //    先挡住，免得一次笔误在上层冒成 500。空串是合法取值（表示首块），不校验。
        if (requirement.PreviousHash is { Length: > 0 } givenHash && !SbiHashChain.IsValidHash(givenHash))
        {
            error = SbiErrorHelper.Custom(504, "Invalid capture request: previousHash is not a SHA-256 hex string");
            return false;
        }

        request = new CoreModels.CaptureRequest
        {
            Modality = modality,

            // 请求侧的"不点名"用空集合表达：Unknown 是结果侧"设备判不出部位"的取值，不进请求。
            Positions = named,
            Group = group,
            Count = count,
            Timeout = timeoutMs,
            RequestedScore = ParseScore(requirement.RequestedScore),
        };

        requested = named;
        return true;
    }

    /// <summary>
    /// 解析请求里的最低分数（线格式是字符串）
    /// </summary>
    /// <param name="requestedScore">
    /// 请求里的字符串
    /// </param>
    /// <returns>
    /// 解析出的数值；缺失或非法返回 null
    /// </returns>
    /// <remarks>
    /// 给<b>设备侧</b>用的数值；报文里那个字符串由 <see cref="SbiScore"/> 承载
    /// （见 <see cref="SbiScore.FromRequested"/>），两者各自解析一次、不共用中间结果。
    /// </remarks>
    private static double? ParseScore(string? requestedScore)
        => double.TryParse(requestedScore, NumberStyles.Float, CultureInfo.InvariantCulture, out var score)
            ? score
            : null;

    /// <summary>
    /// 请求方给的事务号是否合规范
    /// </summary>
    /// <param name="transactionId">
    /// 报文里的事务号
    /// </param>
    /// <returns>
    /// 合规返回 true
    /// </returns>
    /// <remarks>
    /// <para>
    /// 规范（SBI 的 <c>transactionId</c> 一节）给的是：长度 4~50，字符限于字母、数字与连字符，
    /// "If the above validations are not met then an error can be thrown"。CTK 的非法事务号类用例
    /// （如 SBI1110）发的就是一个 62 字符的串，并用 <c>TransactionIdErrorRCaptureResponseSchema</c>
    /// 把响应的码与描述都钉死成 <c>112</c> / <c>Invalid Transaction ID</c>。
    /// </para>
    /// <para>
    /// <b>缺失也算不合规</b>：规范把它列为必填，CTK 的请求 schema 也在 <c>required</c> 里。
    /// </para>
    /// <para>
    /// 纯函数，单列出来是为了能脱离真机直接断言。
    /// </para>
    /// </remarks>
    internal static bool IsValidTransactionId(string? transactionId)
        => transactionId is { Length: >= 4 and <= 50 }
            && transactionId.All(static character => char.IsAsciiLetterOrDigit(character) || character == '-');

    /// <summary>
    /// 失败响应：单元素、只表达错误
    /// </summary>
    /// <param name="error">
    /// 错误对象
    /// </param>
    /// <returns>
    /// 单元素响应
    /// </returns>
    /// <remarks>
    /// <b>失败条目仍填全 <c>specVersion</c>/<c>data</c>/<c>hash</c></b>：协议的条目形状是固定的，
    /// 失败只是错误对象有值；<c>data</c>/<c>hash</c> 取空串。
    /// </remarks>
    private static RegistrationCaptureResponse Failure(SbiError error)
        => new() { Biometrics = [ErrorEntry(error)] };

    /// <summary>
    /// 请求报文本身不可用（解析不了、含未知属性、缺必填）时的失败响应
    /// </summary>
    /// <param name="reason">
    /// 原因描述，进错误对象的 <c>errorInfo</c>
    /// </param>
    /// <returns>
    /// 单元素错误响应
    /// </returns>
    /// <remarks>
    /// <para>
    /// 放在本类是因为响应的形状归这里，宿主只负责把"解析失败"转交过来 —— 它拿不到也不该拼协议报文。
    /// </para>
    /// <para>
    /// 取 5xx 段是本类既有做法（"bio 为空"也走 <c>Custom(504, ...)</c>）；CTK 的
    /// <c>ErrorCaptureResponseSchema</c> 只要求码是 3 位非 0、描述不是 <c>Success</c>。
    /// </para>
    /// </remarks>
    public static RegistrationCaptureResponse InvalidRequest(string reason)
        => Failure(SbiErrorHelper.Custom(504, reason));

    /// <summary>
    /// 构造一个错误条目
    /// </summary>
    /// <param name="error">
    /// 错误对象
    /// </param>
    /// <returns>
    /// 错误条目
    /// </returns>
    private static RegistrationCaptureResponseBiometric ErrorEntry(SbiError error) => new()
    {
        SpecVersion = SbiSpec.Version,
        Data = string.Empty,
        Hash = string.Empty,
        Error = error,
    };

    /// <summary>
    /// 把采集时刻格式化成报文写法
    /// </summary>
    /// <param name="capturedAt">
    /// 采集时刻
    /// </param>
    /// <returns>
    /// ISO 8601 时刻（秒级、UTC、带 Z）
    /// </returns>
    /// <remarks>
    /// 与 digitalId 的 <c>dateTime</c> 同一写法（秒级、UTC、<c>Z</c>、不带毫秒）—— 规范对
    /// <c>timestamp</c> 的格式要求与之相同。
    /// </remarks>
    private static string FormatTimestamp(DateTimeOffset capturedAt)
        => capturedAt.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
