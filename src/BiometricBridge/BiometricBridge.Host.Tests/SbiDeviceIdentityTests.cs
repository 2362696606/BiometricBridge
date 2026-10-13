using System.Buffers.Text;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using BiometricBridge.Core;
using BiometricBridge.Core.Models;
using BiometricBridge.Core.Models.Enums;
using BiometricBridge.Host;
using BiometricBridge.Host.Config;
using BiometricBridge.Host.Crypto;
using BiometricBridge.Host.Dto;
using JetBrains.Annotations;
using Xunit;
using CoreModels = BiometricBridge.Core.Models;

namespace BiometricBridge.Host.Tests;

/// <summary>
/// 发现与设备信息必须报<b>同一个</b>设备身份
/// </summary>
/// <remarks>
/// <para>
/// 守的是真机上栽过的那一跤：MOSIP 注册客户端拿到 <c>/info</c> 的 <c>deviceId</c> 后，会重新发一次发现请求，
/// 拿它去响应里逐字段比对（<c>MosipDeviceSpecification_095_ProviderImpl.isDeviceAvailable</c>：状态、
/// 认证等级、<c>deviceCode</c> 与 <c>deviceId</c> 都要对上），对不上就把设备从它的注册表里摘掉，
/// 界面随即报 <c>NO_DEVICE_FOUND</c>（"Device is not available"）。
/// </para>
/// <para>
/// 我们两个端点曾一个报内部 GUID、一个报序列号 —— 单看每个响应都合法，配起来必错，故这条钉住"同源"。
/// </para>
/// </remarks>
[TestSubject(typeof(DeviceIdentityService))]
public sealed class SbiDeviceIdentityTests
{
    /// <summary>测试设备的序列号。</summary>
    private const string SerialNo = "28688711000025C1";

    /// <summary>回呼根地址（形状与宿主给的一致）。</summary>
    private const string CallbackBaseUrl = "http://127.0.0.1:4501/";

    /// <summary>发现的请求类型：本设备是虹膜设备。</summary>
    private const string IrisType = "Iris";

    /// <summary>
    /// 测试设备的 id：<b>拔插前后必须是同一个</b> —— 身份缓存就是按它记的
    /// </summary>
    /// <remarks>
    /// 真机上的 id 由设备管理器按设备稳定分配，拔了再插还是它；测试里若每次 <c>NewGuid</c>，
    /// "断开后仍报同一个身份"就无从成立（那是测试写错，不是被测行为错）。
    /// </remarks>
    private static readonly Guid DeviceGuid = Guid.NewGuid();

    /// <summary>
    /// 在线设备：两端点报同一个身份，且与 <c>deviceCode</c> 一致
    /// </summary>
    [Fact]
    public void 在线设备两端点报同一个身份()
    {
        var inventory = new SingleDeviceInventory(Online(SerialNo));
        var identity = new DeviceIdentityService();

        var discovery = Assert.IsType<DiscoveryResponse>(
            Assert.Single(new DeviceDiscoveryService(inventory, identity).GetDiscovery(IrisType, CallbackBaseUrl)));
        var info = Assert.Single(
            new DeviceInfoService(inventory, UnusableSigner(), identity, new MosipOptions()).GetDeviceInfo(CallbackBaseUrl));

        Assert.Equal(SerialNo, discovery.DeviceId);
        Assert.Equal(SerialNo, discovery.DeviceCode);
        Assert.Equal(discovery.DeviceId, DeviceIdOf(info.DeviceInfo));
        Assert.Equal(SerialNo, SerialNoOf(discovery.DigitalId));
    }

    /// <summary>
    /// 设备拔掉之后：两端点仍报同一个身份（取自最近一次见到的序列号）
    /// </summary>
    /// <remarks>
    /// 序列号只有连着时才由设备报出，而"把设备拔掉"恰是造 <c>Not Ready</c> 最常规的做法 ——
    /// 这时两端点若各记各的（或都不记），又会分家，客户端同样认不出设备。
    /// </remarks>
    [Fact]
    public void 断开后两端点仍报同一个身份()
    {
        var identity = new DeviceIdentityService();

        // 先在线跑一轮：让身份被记下（两个端点共用同一份身份）。
        var online = new SingleDeviceInventory(Online(SerialNo));
        _ = new DeviceDiscoveryService(online, identity).GetDiscovery(IrisType, CallbackBaseUrl);
        _ = new DeviceInfoService(online, UnusableSigner(), identity, new MosipOptions()).GetDeviceInfo(CallbackBaseUrl);

        // 再拔掉：这时设备什么都报不出来。
        var offline = new SingleDeviceInventory(Offline());
        var discovery = Assert.IsType<DiscoveryResponse>(
            Assert.Single(new DeviceDiscoveryService(offline, identity).GetDiscovery(IrisType, CallbackBaseUrl)));
        var info = Assert.Single(
            new DeviceInfoService(offline, UnusableSigner(), identity, new MosipOptions()).GetDeviceInfo(CallbackBaseUrl));

        Assert.Equal(SerialNo, discovery.DeviceId);
        Assert.Equal(discovery.DeviceId, DeviceIdOf(info.DeviceInfo));
    }

    /// <summary>
    /// 连身份都拿不到的设备：两端点一致地略过它
    /// </summary>
    /// <remarks>
    /// 序列号从没报过时，<c>deviceId</c>/<c>deviceCode</c>/内嵌 <c>serialNo</c> 三处都拼不出非空值
    /// （CTK 的 schema 要求 <c>non-empty-string</c>）。这种设备只能略过，且两个端点要<b>一致地</b>略过 ——
    /// 只有一边报出来，客户端就会拿着一个查不到对应的 id。
    /// </remarks>
    [Fact]
    public void 从没报过序列号的设备两端点都略过()
    {
        var inventory = new SingleDeviceInventory(Online(serialNo: null));
        var identity = new DeviceIdentityService();

        Assert.Empty(new DeviceDiscoveryService(inventory, identity).GetDiscovery(IrisType, CallbackBaseUrl));
        Assert.Empty(
            new DeviceInfoService(inventory, UnusableSigner(), identity, new MosipOptions()).GetDeviceInfo(CallbackBaseUrl));
    }

    /// <summary>
    /// 取 JWS 载荷里的 <c>deviceId</c>
    /// </summary>
    /// <param name="jws">
    /// JWS 串（未签名形态即裸载荷）
    /// </param>
    /// <returns>
    /// 载荷里的 <c>deviceId</c>
    /// </returns>
    private static string DeviceIdOf(string jws) => PayloadField(jws, "deviceId");

    /// <summary>
    /// 取数字标识（<c>digitalId</c>）载荷里的 <c>serialNo</c>
    /// </summary>
    /// <param name="digitalId">
    /// 数字标识串
    /// </param>
    /// <returns>
    /// 载荷里的 <c>serialNo</c>
    /// </returns>
    private static string SerialNoOf(string digitalId) => PayloadField(digitalId, "serialNo");

    /// <summary>
    /// 解开 JWS 载荷并取一个字段
    /// </summary>
    /// <param name="jws">
    /// JWS 串
    /// </param>
    /// <param name="field">
    /// 字段名
    /// </param>
    /// <returns>
    /// 字段取值
    /// </returns>
    private static string PayloadField(string jws, string field)
    {
        // 未签名是裸载荷、已签名是三段，故按段数取，而不是固定取第二段。
        var segments = jws.Split('.');
        var payload = segments.Length == 3 ? segments[1] : segments[0];

        using var document = JsonDocument.Parse(Base64Url.DecodeFromChars(payload).ToArray());
        return document.RootElement.GetProperty(field).GetString()!;
    }

    /// <summary>
    /// 在线的虹膜设备快照
    /// </summary>
    /// <param name="serialNo">
    /// 设备当前报出的序列号；null 表示报不出（从没报过）
    /// </param>
    /// <returns>
    /// 快照
    /// </returns>
    private static CoreModels.DeviceSnapshot Online(string? serialNo) => new(
        DeviceGuid,
        Info(serialNo),
        BiometricDeviceStatus.Ready,
        IsConnected: true);

    /// <summary>
    /// 拔掉后的虹膜设备快照（序列号当然报不出来）
    /// </summary>
    /// <returns>
    /// 快照
    /// </returns>
    private static CoreModels.DeviceSnapshot Offline() => new(
        DeviceGuid,
        Info(serialNo: null),
        BiometricDeviceStatus.NotReady,
        IsConnected: false);

    /// <summary>
    /// 一台虹膜设备的静态信息（各项对齐真设备的特性）
    /// </summary>
    /// <param name="serialNo">
    /// 设备当前报出的序列号；null 表示报不出
    /// </param>
    /// <returns>
    /// 设备静态信息
    /// </returns>
    /// <remarks>
    /// 在调用点用对象初始化器给 <c>required</c> 成员赋值（语言要求），故这里只能是工厂方法，不能是构造函数。
    /// </remarks>
    private static TestIrisDeviceInfo Info(string? serialNo) => new()
    {
        SerialNo = serialNo,
        Make = "ICT Global Co., Ltd.",
        Model = "CRH3L160",
        DeviceProvider = "Shenzhen ICT Global",
        DeviceProviderId = "ICT_CTK_DEVICE_PROVIDER",
        DeviceSubIds = [1, 2, 3],
        DeviceSubType = DeviceSubType.Double,
        Certification = CertificationLevel.L0,
        Purpose = Purpose.Registration,
    };

    /// <summary>
    /// 用不了的签名器：DP 材料缺失时 <c>/info</c> 该退到未签名形态
    /// </summary>
    /// <returns>
    /// 签名器提供者
    /// </returns>
    /// <remarks>
    /// 这里刻意让它必然失败（拿不到 DP 证书）—— 未签名形态照样带 <c>deviceId</c>，正是本测试要比对的东西；
    /// 顺带把"签不出就退到未签名"那条路径也走了一遍。
    /// </remarks>
    private static DeviceSignerProvider UnusableSigner() => new(new UnusableIssuer());

    /// <summary>
    /// 只有一台设备的清单
    /// </summary>
    private sealed class SingleDeviceInventory(CoreModels.DeviceSnapshot snapshot) : IDeviceInventory
    {
        /// <inheritdoc/>
        public IReadOnlyList<CoreModels.DeviceSnapshot> GetDeviceSnapshots() => [snapshot];
    }

    /// <summary>
    /// 一台虹膜设备的静态信息
    /// </summary>
    private sealed class TestIrisDeviceInfo : CoreModels.DeviceInfo
    {
        /// <inheritdoc/>
        public override BiometricModality Modality => BiometricModality.Iris;
    }

    /// <summary>
    /// 拿不到材料的签发器
    /// </summary>
    private sealed class UnusableIssuer : IDeviceCertificateIssuer
    {
        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">
        /// 恒抛出：本测试没有 DP 材料
        /// </exception>
        public X509Certificate2 DeviceProviderCertificate =>
            throw new InvalidOperationException("本测试没有 DP 材料。");

        /// <inheritdoc/>
        /// <exception cref="InvalidOperationException">
        /// 恒抛出：本测试没有 DP 材料
        /// </exception>
        public byte[] Issue(
            System.Security.Cryptography.RSA deviceKey,
            string serialNo,
            string makeModel,
            string deviceProvider)
            => throw new InvalidOperationException("本测试没有 DP 材料。");
    }
}
