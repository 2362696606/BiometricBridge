using BiometricBridge.Core;
using BiometricBridge.Host.Config;
using BiometricBridge.Host.Crypto;
using BiometricBridge.Host.Dto;
using BiometricBridge.Host.Dto.Enum;
using CoreModels = BiometricBridge.Core.Models;

namespace BiometricBridge.Host;

/// <summary>
/// SBI 设备信息：把本机设备清单翻成 <c>/info</c> 的响应报文
/// </summary>
/// <remarks>
/// <para>
/// 不引任何 HTTP 依赖 —— 端口、路由、响应头都是宿主的事，这里只按规范拼响应体。
/// </para>
/// <para>
/// 与 <see cref="DeviceDiscoveryService"/> 的关键差别：本端点的 <c>deviceInfo</c> 是一串 <b>JWS</b>
/// （已注册设备由设备私钥签名），而发现端点把各项平铺在数组元素上、且数字标识不签名。
/// </para>
/// <para>
/// <b>已注册 / 未注册按设备状态二分</b>：只有 <c>NotRegistered</c> 走未签名形态，
/// <c>Ready</c>/<c>Busy</c>/<c>NotReady</c> 都算已注册。规范按"注册与否"定形态，而不是按"就绪与否"。
/// </para>
/// <para>
/// <b>每台设备都产出元素</b>：CTK 的四份 schema 都要求 <c>minItems: 1</c>，略过任何一台都可能把数组弄空。
/// </para>
/// </remarks>
public sealed class DeviceInfoService
{
    #region Fileds

    /// <summary>
    /// 设备清单
    /// </summary>
    private readonly IDeviceInventory _inventory;

    /// <summary>
    /// 签名器提供者
    /// </summary>
    private readonly DeviceSignerProvider _signers;

    /// <summary>
    /// 报文里 <c>env</c> 的取值
    /// </summary>
    private readonly DeviceEnvironment _env;

    /// <summary>
    /// 各设备最近一次报出的序列号
    /// </summary>
    /// <remarks>
    /// <b>只记这一项，其余信息不缓存</b>：设备离了连接，厂商/型号/子类型照样拿得到（都在特性上），
    /// 唯独序列号是连接期间才由设备报出的。
    /// <para>
    /// <b>为什么必须记</b>：CTK 把 <c>deviceInfo</c> 解开后逐项校验，<c>deviceInfoDecoded.deviceId</c>、
    /// <c>deviceInfoDecoded.deviceCode</c> 以及<b>内层</b> <c>digitalIdDecoded.serialNo</c> 都要求
    /// <c>non-empty-string</c>；2026-10-09 实测留空被判 <c>must be at least 1 characters long</c>。
    /// 而"把设备拔掉"恰是造 <c>Not Ready</c> 最常规的做法 —— 不记它，那条路径就报不出合法元素。
    /// 参考实现同样留了一份 lastKnownInfo。
    /// </para>
    /// </remarks>
    private readonly Dictionary<Guid, string> _lastKnownSerialNumbers = [];

    /// <summary>
    /// 保护上面的字典（<c>/info</c> 可以被并发调用）
    /// </summary>
    private readonly object _gate = new();

    #endregion

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="inventory">
    /// 设备清单
    /// </param>
    /// <param name="signers">
    /// 签名器提供者
    /// </param>
    /// <param name="options">
    /// MOSIP 侧设置（取其中的 <c>env</c>）
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// 任一参数为 null
    /// </exception>
    public DeviceInfoService(IDeviceInventory inventory, DeviceSignerProvider signers, MosipOptions options)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(signers);
        ArgumentNullException.ThrowIfNull(options);

        _inventory = inventory;
        _signers = signers;

        // 配置里写的是枚举成员名；写错就退回规范允许的默认环境，不让一个笔误把端点整个打挂。
        _env = Enum.TryParse<DeviceEnvironment>(options.Env, ignoreCase: true, out var env)
            ? env
            : DeviceEnvironment.Staging;
    }

    /// <summary>
    /// 生成设备信息响应
    /// </summary>
    /// <param name="callbackBaseUrl">
    /// 回呼根地址，形如 <c>http://127.0.0.1:4501/</c>（含结尾斜杠）。由宿主给出 —— 端口是它的事
    /// </param>
    /// <returns>
    /// 设备信息响应体，每台设备一条。规范要求是数组
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="callbackBaseUrl"/> 为 null
    /// </exception>
    public IReadOnlyList<DeviceInfoResponse> GetDeviceInfo(string callbackBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(callbackBaseUrl);

        var responses = new List<DeviceInfoResponse>();
        foreach (var snapshot in _inventory.GetDeviceSnapshots())
        {
            // 设备类漏标设备信息特性：拿不到厂商/型号，报文无从拼装，只能略过。
            if (snapshot.Info is not { } deviceInfo)
            {
                continue;
            }

            if (CreateResponse(snapshot, deviceInfo, callbackBaseUrl) is { } response)
            {
                responses.Add(response);
            }
        }

        return responses;
    }

    /// <summary>
    /// 拼一条设备信息
    /// </summary>
    /// <param name="snapshot">
    /// 设备快照
    /// </param>
    /// <param name="deviceInfo">
    /// 设备静态信息
    /// </param>
    /// <param name="callbackBaseUrl">
    /// 回呼根地址
    /// </param>
    /// <returns>
    /// 设备信息响应元素；连身份都拿不到时返回 null（调用方略过该设备）
    /// </returns>
    /// <remarks>
    /// <b>能签就签，签不出就退到未签名形态。</b>设备证书的 <c>CN</c> 是序列号，而设备只在连接期间
    /// 报得出它，所以"没连上"的设备签不出证书；除 Ready 外的那三份 schema 并不要求签名
    /// （<c>deviceInfo</c> 只当普通字符串），未签名形态即可满足。设备断开时 <c>deviceStatus</c>
    /// 会映射成 <c>Not Ready</c>、错误码取 110。
    /// <para>
    /// <b>从没连过的设备只能略过</b>：序列号拿不到，而它同时是 <c>deviceId</c>/<c>deviceCode</c>/内嵌
    /// <c>serialNo</c> 三项的取值，三者都要求非空。连过之后即便断开，靠
    /// <see cref="_lastKnownSerialNumbers"/> 也照样能报。
    /// </para>
    /// </remarks>
    private DeviceInfoResponse? CreateResponse(
        CoreModels.DeviceSnapshot snapshot,
        CoreModels.DeviceInfo deviceInfo,
        string callbackBaseUrl)
    {
        var status = SbiMapping.MapStatus(snapshot);
        var serialNo = ResolveSerialNo(snapshot.DeviceId, deviceInfo);

        // 这台从没报过序列号：拼不出合法的 deviceId/deviceCode/serialNo，只能略过。
        if (serialNo.Length == 0)
        {
            return null;
        }

        var digitalId = SbiDigitalIdFactory.Create(deviceInfo, serialNo);

        // 未注册设备的用途留空（规范：未注册时 purpose 为空串）。
        var purpose = status == DeviceStatus.NotRegistered
            ? SbiSpec.EmptyPurpose
            : deviceInfo.Purpose.ToString();

        // 未注册设备明确不签名；其余状态能签就签（DP 材料出问题时退到未签名形态）。
        IJwsSigner? signer = null;
        if (status != DeviceStatus.NotRegistered)
        {
            try
            {
                signer = _signers.GetSigner(serialNo, deviceInfo.Make, deviceInfo.Model, deviceInfo.DeviceProvider);
            }
            catch (Exception)
            {
                signer = null;
            }
        }

        var payload = CreatePayload(
            deviceInfo,
            status,
            purpose,
            serialNo,
            signer is null ? UnsignedJwt.Encode(digitalId) : signer.SignDigitalId(digitalId),
            callbackBaseUrl);

        return new DeviceInfoResponse
        {
            DeviceInfo = signer is null ? UnsignedJwt.Encode(payload) : signer.SignDeviceInfo(payload),
            Error = SbiMapping.ErrorFor(status),
        };
    }

    /// <summary>
    /// 取该设备的序列号
    /// </summary>
    /// <param name="deviceId">
    /// 设备 id
    /// </param>
    /// <param name="deviceInfo">
    /// 设备静态信息
    /// </param>
    /// <returns>
    /// 序列号；从没报过则为空串
    /// </returns>
    /// <remarks>
    /// 设备当前报的优先，并顺手记下来；当前报不出（未连接）就用上次见过的。
    /// </remarks>
    private string ResolveSerialNo(Guid deviceId, CoreModels.DeviceInfo deviceInfo)
    {
        lock (_gate)
        {
            if (deviceInfo.SerialNo is { Length: > 0 } current)
            {
                _lastKnownSerialNumbers[deviceId] = current;
                return current;
            }

            return _lastKnownSerialNumbers.GetValueOrDefault(deviceId, string.Empty);
        }
    }

    /// <summary>
    /// 拼 deviceInfo 载荷
    /// </summary>
    /// <param name="deviceInfo">
    /// 设备静态信息
    /// </param>
    /// <param name="status">
    /// 协议状态
    /// </param>
    /// <param name="purpose">
    /// 用途；未注册时为空串
    /// </param>
    /// <param name="serialNo">
    /// 设备序列号；未连接时为空串
    /// </param>
    /// <param name="digitalId">
    /// 内嵌的数字标识：已注册时是签名 JWS，未注册时是未签名串
    /// </param>
    /// <param name="callbackBaseUrl">
    /// 回呼根地址
    /// </param>
    /// <returns>
    /// deviceInfo 载荷
    /// </returns>
    private DeviceInfo CreatePayload(
        CoreModels.DeviceInfo deviceInfo,
        DeviceStatus status,
        string purpose,
        string serialNo,
        string digitalId,
        string callbackBaseUrl)
    {
        return new DeviceInfo
        {
            // 一台设备只暴露一个身份：deviceId 与 deviceCode 都取序列号。
            DeviceId = serialNo,
            DeviceStatus = status,
            // L0 设备的固件版本即服务版本（规范原话："In case of L0 this is same as serviceVersion."）。
            Firmware = SbiSpec.Version,
            Certification = SbiMapping.MapCertification(deviceInfo.Certification),
            ServiceVersion = SbiSpec.Version,
            DeviceSubId = SbiMapping.MapDeviceSubId(deviceInfo.DeviceSubIds),
            CallbackId = callbackBaseUrl,
            DigitalId = digitalId,
            DeviceCode = serialNo,
            Env = _env,
            SpecVersion = [SbiSpec.Version],
            Purpose = purpose,
        };
    }
}
