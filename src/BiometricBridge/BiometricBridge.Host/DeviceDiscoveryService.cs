using BiometricBridge.Core;
using BiometricBridge.Host.Common;
using BiometricBridge.Host.Dto;
using BiometricBridge.Host.Dto.Enum;
using CoreEnums = BiometricBridge.Core.Models.Enums;
using CoreModels = BiometricBridge.Core.Models;

namespace BiometricBridge.Host;

/// <summary>
/// SBI 设备发现：把本机设备清单翻成 <c>/device</c> 的响应报文
/// </summary>
/// <remarks>
/// <para>
/// 不引任何 HTTP 依赖 —— 端口、路由、响应头都是宿主的事，这里只按规范拼响应体，故换一种对外服务方式
/// （gRPC、命名管道……）时本类原样不动。
/// </para>
/// <para>
/// 设备清单由 <see cref="IDeviceInventory"/> 提供，本类不持有设备，也就不碰设备。
/// </para>
/// <para>
/// 报文类型取自 <c>BiometricBridge.Host.Dto</c>，与设备抽象层的模型只在 <see cref="SbiMapping"/> 处对接。
/// </para>
/// <para>
/// <b>两种形状</b>：正常是每条设备一条 <see cref="DiscoveryResponse"/>；请求类型非法时是单元素的
/// <see cref="DiscoveryErrorResponse"/>。协议为两者各定了一份 schema，故这里的返回类型是
/// <c>IReadOnlyList&lt;object&gt;</c>（序列化按运行时类型走）。
/// </para>
/// </remarks>
public sealed class DeviceDiscoveryService
{
    #region Fileds

    /// <summary>
    /// 设备清单
    /// </summary>
    private readonly IDeviceInventory _inventory;

    #endregion

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="inventory">
    /// 设备清单
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="inventory"/> 为 null
    /// </exception>
    public DeviceDiscoveryService(IDeviceInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(inventory);

        _inventory = inventory;
    }

    /// <summary>
    /// 生成发现响应
    /// </summary>
    /// <param name="type">
    /// 请求里的设备类型，取值见 <see cref="TryParseType"/>
    /// </param>
    /// <param name="callbackBaseUrl">
    /// 回呼根地址，形如 <c>http://127.0.0.1:4501/</c>（含结尾斜杠）。由宿主给出 —— 端口是它的事
    /// </param>
    /// <returns>
    /// 发现响应体，每台匹配的设备一条；类型非法时为单元素错误记录。规范要求是数组
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="callbackBaseUrl"/> 为 null
    /// </exception>
    public IReadOnlyList<object> GetDiscovery(string? type, string callbackBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(callbackBaseUrl);

        if (!TryParseType(type, out var requested))
        {
            // 类型非法：协议为这件事定了另一种形状，且仍走 HTTP 200（不是 400）。
            return [InvalidTypeEntry()];
        }

        var modality = ToModality(requested);

        var responses = new List<object>();
        foreach (var snapshot in _inventory.GetDeviceSnapshots())
        {
            // 设备类漏标设备信息特性：拿不到厂商/型号，digitalId 就无从拼装，只能略过。
            if (snapshot.Info is not { } deviceInfo)
            {
                continue;
            }

            // 规范要求只回类型匹配的设备；不限模态时全回。
            if (modality is { } requestedModality && deviceInfo.Modality != requestedModality)
            {
                continue;
            }

            responses.Add(CreateResponse(snapshot, deviceInfo, callbackBaseUrl));
        }

        return responses;
    }

    /// <summary>
    /// 解析请求里的设备类型
    /// </summary>
    /// <param name="type">
    /// 请求里那条字符串
    /// </param>
    /// <param name="parsed">
    /// 解析出的类型
    /// </param>
    /// <returns>
    /// 取值合法返回 true
    /// </returns>
    /// <remarks>
    /// <b>大小写严格</b>：规范的取值就是这四个字面量，而 CTK 有一条负向用例专发大写
    /// （SBI1196 "Discover request attributes in UPPER CASE"），期望 SBI 回错误态。宽容地把大写当合法，
    /// 那条用例必然失败。缺失也按非法处理 —— 规范把该字段列为必填。
    /// </remarks>
    private static bool TryParseType(string? type, out DiscoveryType parsed)
    {
        switch (type)
        {
            case "Biometric Device":
                parsed = DiscoveryType.BiometricDevice;
                return true;
            case "Finger":
                parsed = DiscoveryType.Finger;
                return true;
            case "Iris":
                parsed = DiscoveryType.Iris;
                return true;
            case "Face":
                parsed = DiscoveryType.Face;
                return true;
            default:
                parsed = default;
                return false;
        }
    }

    /// <summary>
    /// 拼一条设备记录
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
    /// 设备记录
    /// </returns>
    private static DiscoveryResponse CreateResponse(
        CoreModels.DeviceSnapshot snapshot,
        CoreModels.DeviceInfo deviceInfo,
        string callbackBaseUrl)
    {
        var status = SbiMapping.MapStatus(snapshot);

        return new DiscoveryResponse
        {
            DeviceId = snapshot.DeviceId.ToString(),
            DeviceStatus = status,
            Certification = SbiMapping.MapCertification(deviceInfo.Certification),

            // 与 /info 取同一个值（规范版本）：参考实现两个端点都这么报，是过 CTK 的实证。
            ServiceVersion = SbiSpec.Version,
            DeviceSubId = SbiMapping.MapDeviceSubId(deviceInfo.DeviceSubIds),
            CallbackId = callbackBaseUrl,

            // 发现端点的数字标识一律未签名（发现发生在信任建立之前）。
            DigitalId = UnsignedJwt.Encode(SbiDigitalIdFactory.Create(deviceInfo, deviceInfo.SerialNo ?? string.Empty)),

            // 设备连上后才报 SN，故未连接时为空串：线格式要求该字段出现，而空串是有值的。
            DeviceCode = deviceInfo.SerialNo ?? string.Empty,
            SpecVersion = [SbiSpec.Version],

            // 未注册设备的用途必须是空串：CTK 以 JSON Schema 的 not:{enum:[Auth,Registration]} 校验此字段。
            Purpose = status == DeviceStatus.NotRegistered
                ? SbiSpec.EmptyPurpose
                : deviceInfo.Purpose.ToString(),

            // 就绪时取表示成功的码：正向 schema 把 error 钉成固定 enum ["0"]/["Success"] 并列为必填。
            Error = SbiMapping.ErrorFor(status),
        };
    }

    /// <summary>
    /// 请求类型非法时的响应
    /// </summary>
    /// <returns>
    /// 单元素数组，只表达错误
    /// </returns>
    private static DiscoveryErrorResponse InvalidTypeEntry() => new()
    {
        // 5xx 段是规范留给"设备供应商自定"的；该码满足错误态 schema 的 ^[1-9][0-9][0-9]$。
        Error = SbiErrorHelper.Custom(504, "Invalid JSON Value Type For Discovery"),
    };

    /// <summary>
    /// 请求类型 → 要匹配的模态
    /// </summary>
    /// <param name="type">
    /// 请求里的设备类型
    /// </param>
    /// <returns>
    /// 要匹配的模态；不限模态时返回 null
    /// </returns>
    private static CoreEnums.BiometricModality? ToModality(DiscoveryType type) => type switch
    {
        DiscoveryType.Finger => CoreEnums.BiometricModality.Finger,
        DiscoveryType.Face => CoreEnums.BiometricModality.Face,
        DiscoveryType.Iris => CoreEnums.BiometricModality.Iris,
        _ => null,
    };
}
