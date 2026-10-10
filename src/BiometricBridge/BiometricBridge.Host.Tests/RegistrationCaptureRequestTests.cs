using System.Text.Json;
using BiometricBridge.Host.Dto;
using BiometricBridge.Host.Dto.Enum;
using JetBrains.Annotations;
using Xunit;

namespace BiometricBridge.Host.Tests;

/// <summary>
/// <see cref="RegistrationCaptureRequest"/> 的反序列化契约
/// </summary>
/// <remarks>
/// <para>
/// 守两件事，方向相反：
/// </para>
/// <list type="number">
/// <item>
/// CTK 实发的合法请求必须能解出来 —— 严格模式一旦把规范允许的字段当成多余，每次采集都会变成错误响应。
/// 下面的 <see cref="CtkRegistrationRequest"/> 是 CTK 跑 SBI1050 时实发的那份（原样抄录）。
/// </item>
/// <item>
/// 规范没定义的属性必须被拒 —— 请求方把字段名写错（CTK 的 SBI1097/SBI1099 就是这么构造的）时，
/// 静默丢掉只会变成"照采了一趟"、客户端还拿到成功。
/// </item>
/// </list>
/// </remarks>
[TestSubject(typeof(RegistrationCaptureRequest))]
public class RegistrationCaptureRequestTests
{
    /// <summary>CTK 跑 SBI1050 时实发的请求体（原样）。</summary>
    private const string CtkRegistrationRequest =
        """
        {"env":"Developer","purpose":"Registration","specVersion":"0.9.5","timeout":"10000",
         "captureTime":"2026-10-10T05:43:24.905Z","transactionId":"SBI1050-905",
         "bio":[{"type":"Iris","count":"2","exception":[],"requestedScore":"100",
                 "deviceId":"f107a017-1563-4b02-992f-494fd75f5a0c","deviceSubId":"3",
                 "previousHash":"e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
                 "bioSubType":["Left","Right"]}],
         "customOpts":null}
        """;

    [Fact]
    public void CTK的合法请求解得出来()
    {
        var request = Parse(CtkRegistrationRequest);

        Assert.Equal(DeviceEnvironment.Developer, request.Env);
        Assert.Equal(Purpose.Registration, request.Purpose);
        Assert.Equal("0.9.5", request.SpecVersion);
        Assert.Equal(10000, request.Timeout);
        Assert.Equal("SBI1050-905", request.TransactionId);

        var bio = Assert.Single(request.Bio!);
        Assert.Equal(BiometricType.Iris, bio.Type);
        Assert.Equal(2, bio.Count);
        Assert.Equal(3, bio.DeviceSubId);
        Assert.Equal(["Left", "Right"], bio.BioSubType!);
        Assert.Equal("100", bio.RequestedScore);
        Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", bio.PreviousHash);
    }

    [Fact]
    public void 信封上的未知属性被拒()
    {
        // CTK 的 SBI1097：把 purpose 改名成 purposeXXX。
        var json = CtkRegistrationRequest.Replace("\"purpose\":", "\"purposeXXX\":");

        Assert.Throws<JsonException>(() => Parse(json));
    }

    [Fact]
    public void bio元素上的未知属性被拒()
    {
        // CTK 的 SBI1099：把 bio[0].count 改名成 countXXX。
        // 漏了这条更隐蔽 —— 信封是干净的，只有元素里多一个；
        // 而 count 一缺省，服务就会按 bioSubType 的个数把它推出来，于是照样去采。
        var json = CtkRegistrationRequest.Replace("\"count\":", "\"countXXX\":");

        Assert.Throws<JsonException>(() => Parse(json));
    }

    /// <summary>
    /// 按生产路径解析（与 <c>CaptureController</c> 用的是同一套选项）。
    /// </summary>
    private static RegistrationCaptureRequest Parse(string json)
        => JsonSerializer.Deserialize<RegistrationCaptureRequest>(json, SbiJson.Options)!;
}
