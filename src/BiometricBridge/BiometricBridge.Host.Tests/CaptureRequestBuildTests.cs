using BiometricBridge.Core.Models;
using BiometricBridge.Core.Models.Enums;
using BiometricBridge.Host;
using BiometricBridge.Host.Dto;
using BiometricBridge.Host.Dto.Enum;
using JetBrains.Annotations;
using Xunit;

namespace BiometricBridge.Host.Tests;

/// <summary>
/// <see cref="RegistrationCaptureService"/> 上"碰设备之前"的那些校验的单元测试
/// </summary>
/// <remarks>
/// 守的是"请求自洽性校验"：规范要求 SBI 发现摆放与 <c>deviceSubId</c> 不符、数量与手头能力不符、
/// 事务号不合规时即报错，而这些校验若写漏，坏请求会一路走到真机上白采一趟 —— 且 CTK 有专门用例盯它们。
/// </remarks>
[TestSubject(typeof(RegistrationCaptureService))]
public class CaptureRequestBuildTests
{
    /// <summary>指纹设备广播的子 ID（左四指／右四指／双拇指）。</summary>
    private static readonly int[] FingerSubIds = [1, 2, 3];

    /// <summary>虹膜设备广播的子 ID（只有"不指定"）。</summary>
    private static readonly int[] IrisSubIds = [0];

    [Fact]
    public void 虹膜双眼点名原样传下去()
    {
        Assert.True(Build(
            Iris(subTypes: ["Left", "Right"], count: 2, deviceSubId: 3),
            IrisSubIds,
            out var request,
            out var requested,
            out _));

        Assert.Equal([BiometricPosition.LeftIris, BiometricPosition.RightIris], request.Positions);
        Assert.Equal([BiometricPosition.LeftIris, BiometricPosition.RightIris], requested);
        Assert.Equal(CaptureGroup.Both, request.Group);
        Assert.Equal(2, request.Count);
    }

    [Fact]
    public void 虹膜未点名却要两枚时按双眼要()
    {
        // 规范允许"任意若干枚"（bioSubType 传 UNKNOWN）；虹膜一共两只，"任意两枚"就是双眼。
        Assert.True(Build(
            Iris(subTypes: [BiometricSubTypes.Unknown, BiometricSubTypes.Unknown], count: 2),
            IrisSubIds,
            out var request,
            out var requested,
            out _));

        Assert.Empty(requested);
        Assert.Equal(CaptureGroup.Both, request.Group);
    }

    [Fact]
    public void 虹膜未点名只要一枚时不指定眼睛()
    {
        Assert.True(Build(
            Iris(subTypes: [BiometricSubTypes.Unknown], count: 1),
            IrisSubIds,
            out var request,
            out var requested,
            out _));

        Assert.Empty(requested);
        Assert.Equal(CaptureGroup.Any, request.Group);
    }

    [Fact]
    public void 数量缺省时按子类型个数归一()
    {
        Assert.True(Build(
            Iris(subTypes: ["Left", "Right"]),
            IrisSubIds,
            out var request,
            out _,
            out _));

        Assert.Equal(2, request.Count);
    }

    [Fact]
    public void 分组与点名不符被拒()
    {
        // deviceSubId=1 是左眼，却点名要右眼：规范要求识别出这种不符。
        Assert.False(Build(
            Iris(subTypes: ["Right"], count: 1, deviceSubId: 1),
            IrisSubIds,
            out _,
            out _,
            out var error));

        Assert.Equal("109", error!.ErrorCode);
    }

    [Fact]
    public void 设备没有该子模块时被拒()
    {
        // 设备只广播了左四指，请求却要右四指。
        Assert.False(Build(
            Finger(subTypes: ["Right IndexFinger"], count: 1, deviceSubId: 2),
            [1],
            out _,
            out _,
            out var error));

        Assert.Equal("109", error!.ErrorCode);
    }

    [Fact]
    public void 子ID取值越界被拒()
    {
        Assert.False(Build(Iris(deviceSubId: 9), IrisSubIds, out _, out _, out var error));
        Assert.Equal("109", error!.ErrorCode);
    }

    [Fact]
    public void 认不出的子类型被拒()
    {
        // 大小写与词形都必须逐字对齐规范，写错就是请求方的笔误，不能静默采别的部位。
        Assert.False(Build(Finger(subTypes: ["Left indexFinger"], count: 1), FingerSubIds, out _, out _, out var error));
        Assert.Equal("109", error!.ErrorCode);
    }

    [Fact]
    public void 数量与子类型个数不符被拒()
    {
        Assert.False(Build(Iris(subTypes: ["Left", "Right"], count: 1), IrisSubIds, out _, out _, out var error));
        Assert.Equal("109", error!.ErrorCode);
    }

    [Fact]
    public void 显式要零枚被拒()
    {
        // CTK 的 SBI1011 就是这个形状：count=0、bioSubType 空、两眼全豁免。
        // "要零枚"没有生物特征可交，是个不受支持的数量 —— 必须报错，而不是照样去采。
        Assert.False(Build(
            Iris(subTypes: [], count: 0, deviceSubId: 3),
            IrisSubIds,
            out _,
            out _,
            out var error));

        Assert.Equal("109", error!.ErrorCode);
        Assert.Equal("Requested number of biometric (Finger/IRIS) not supported", error.ErrorInfo);
    }

    [Fact]
    public void 显式要零枚且点了名也被拒()
    {
        Assert.False(Build(Iris(subTypes: ["Left", "Right"], count: 0), IrisSubIds, out _, out _, out var error));
        Assert.Equal("109", error!.ErrorCode);
    }

    [Fact]
    public void 没给数量时不视为零枚()
    {
        // 与上一条相对：缺省表示"不指定"，此时由设备决定采几枚，不该被当成"要零枚"。
        Assert.True(Build(Iris(), IrisSubIds, out var request, out _, out _));
        Assert.Equal(0, request.Count);
        Assert.Equal(CaptureGroup.Any, request.Group);
    }

    [Fact]
    public void 指纹双拇指按规范分组()
    {
        Assert.True(Build(
            Finger(subTypes: ["Left Thumb", "Right Thumb"], count: 2, deviceSubId: 3),
            FingerSubIds,
            out var request,
            out var requested,
            out _));

        Assert.Equal([BiometricPosition.LeftThumb, BiometricPosition.RightThumb], requested);
        Assert.Equal(CaptureGroup.Both, request.Group);
    }

    [Fact]
    public void 指纹左四指按规范分组()
    {
        Assert.True(Build(
            Finger(
                subTypes: ["Left IndexFinger", "Left MiddleFinger", "Left RingFinger", "Left LittleFinger"],
                count: 4,
                deviceSubId: 1),
            FingerSubIds,
            out var request,
            out _,
            out _));

        Assert.Equal(CaptureGroup.Left, request.Group);
        Assert.Equal(4, request.Count);
    }

    [Fact]
    public void 未给子ID时按不指定处理()
    {
        Assert.True(Build(
            Finger(subTypes: ["Left IndexFinger"], count: 1),
            FingerSubIds,
            out var request,
            out _,
            out _));

        Assert.Equal(CaptureGroup.Any, request.Group);
    }

    [Fact]
    public void 虹膜左眼组要两枚被拒()
    {
        // CTK 的 SBI1036：deviceSubId=1（左眼）、bioCount=2、bioSubType 为空。
        // 左眼这一组只可能给出一枚，"要两枚"是个不受支持的数量。
        // 第 4 步只在点名了部位时才对账，此时 bioSubType 为空 —— 漏掉这条会让设备白采一趟还回成功。
        Assert.False(Build(Iris(subTypes: [], count: 2, deviceSubId: 1), IrisSubIds, out _, out _, out var error));

        Assert.Equal("109", error!.ErrorCode);
        Assert.Equal("Requested number of biometric (Finger/IRIS) not supported", error.ErrorInfo);
    }

    [Fact]
    public void 虹膜右眼组要两枚被拒()
    {
        // CTK 的 SBI1037，与上一条左右对称。
        Assert.False(Build(Iris(subTypes: [], count: 2, deviceSubId: 2), IrisSubIds, out _, out _, out var error));
        Assert.Equal("109", error!.ErrorCode);
    }

    [Fact]
    public void 虹膜双眼组要三枚被拒()
    {
        // CTK 的 SBI1038：deviceSubId=3（双眼）、bioCount=3。虹膜一共两只，三枚无从谈起。
        Assert.False(Build(Iris(subTypes: [], count: 3, deviceSubId: 3), IrisSubIds, out _, out _, out var error));
        Assert.Equal("109", error!.ErrorCode);
    }

    [Fact]
    public void 虹膜双眼组要两枚放行()
    {
        // 与上一条相对：CTK 的 SBI1064 / SBI1104 就是这个形状 —— 双眼组要两枚是正着。
        Assert.True(Build(
            Iris(subTypes: [], count: 2, deviceSubId: 3),
            IrisSubIds,
            out var request,
            out _,
            out _));

        Assert.Equal(2, request.Count);
        Assert.Equal(CaptureGroup.Both, request.Group);
    }

    [Fact]
    public void 指纹左四指组要五枚被拒()
    {
        // 容量这条对每个模态都成立，不是虹膜专用：左四指只有四枚。
        Assert.False(Build(Finger(subTypes: [], count: 5, deviceSubId: 1), FingerSubIds, out _, out _, out var error));
        Assert.Equal("109", error!.ErrorCode);
    }

    [Fact]
    public void 人脸要两枚被拒()
    {
        // 人脸没有具名部位（成员表里是空集），规范给它定的是"一次一枚"。
        Assert.False(Build(Face(count: 2), [0], out _, out _, out var error));
        Assert.Equal("109", error!.ErrorCode);
    }

    [Fact]
    public void 畸形的前一哈希被拒()
    {
        // previousHash 会被十六进制解码，形状不对就不是"不自洽"而是根本解不了 ——
        // 不先挡住，一次笔误会在上层冒成 500。
        Assert.False(Build(
            Iris(subTypes: ["Left", "Right"], count: 2, previousHash: "not-a-hash"),
            IrisSubIds,
            out _,
            out _,
            out var error));

        Assert.Equal("504", error!.ErrorCode);
    }

    [Fact]
    public void 空的前一哈希视为首块()
    {
        // 空串是合法取值：表示"没有上一块"，由哈希链自己取空字节的 SHA-256。
        Assert.True(Build(
            Iris(subTypes: ["Left", "Right"], count: 2, previousHash: ""),
            IrisSubIds,
            out _,
            out _,
            out _));
    }

    [Theory]
    [InlineData("SBI1114-567891234554765675346546526738495712345617645432198123")] // CTK SBI1110 实发的那串（62 字符）
    [InlineData("")]                                          // 缺失：规范列为必填
    [InlineData("under_score")]                               // 规范只认字母、数字与连字符
    [InlineData("has space")]
    [InlineData("有中文")]                                     // 非 ASCII 字母
    public void 不合规的事务号被拒(string transactionId)
    {
        Assert.False(RegistrationCaptureService.IsValidTransactionId(transactionId));
    }

    [Fact]
    public void 事务号长度边界()
    {
        // 规范给的是 4~50。用 new string 造串，免得手数字符出错。
        Assert.False(RegistrationCaptureService.IsValidTransactionId(new string('A', 3)));
        Assert.True(RegistrationCaptureService.IsValidTransactionId(new string('A', 4)));
        Assert.True(RegistrationCaptureService.IsValidTransactionId(new string('A', 50)));
        Assert.False(RegistrationCaptureService.IsValidTransactionId(new string('A', 51)));
    }

    [Theory]
    [InlineData("SBI1110")]
    [InlineData("ok-123")]
    [InlineData("123e4567-e89b-12d3-a456-426655440000")]
    public void 合规的事务号放行(string transactionId)
    {
        Assert.True(RegistrationCaptureService.IsValidTransactionId(transactionId));
    }

    /// <summary>
    /// 规范里"任意／不明"的子类型字面量。
    /// </summary>
    private static class BiometricSubTypes
    {
        public const string Unknown = "UNKNOWN";
    }

    /// <summary>
    /// 跑一次请求构建。
    /// </summary>
    private static bool Build(
        RegistrationCaptureRequestBio requirement,
        int[] deviceSubIds,
        out CaptureRequest request,
        out IReadOnlyList<BiometricPosition> requested,
        out SbiError? error)
        => RegistrationCaptureService.TryBuildRequest(
            requirement,
            ModalityOf(requirement),
            deviceSubIds,
            timeoutMs: 10000,
            out request,
            out requested,
            out error);

    /// <summary>
    /// 报文里的生物特征类型 → 设备抽象层的模态。
    /// </summary>
    private static BiometricModality ModalityOf(RegistrationCaptureRequestBio requirement)
        => requirement.Type switch
        {
            BiometricType.Iris => BiometricModality.Iris,
            BiometricType.Face => BiometricModality.Face,
            _ => BiometricModality.Finger,
        };

    /// <summary>
    /// 一份虹膜采集要求。
    /// </summary>
    private static RegistrationCaptureRequestBio Iris(
        string[]? subTypes = null,
        int? count = null,
        int? deviceSubId = null,
        string? previousHash = null) => new()
        {
            Type = BiometricType.Iris,
            BioSubType = subTypes,
            Count = count,
            DeviceSubId = deviceSubId,
            PreviousHash = previousHash,
        };

    /// <summary>
    /// 一份指纹采集要求。
    /// </summary>
    private static RegistrationCaptureRequestBio Finger(
        string[]? subTypes = null,
        int? count = null,
        int? deviceSubId = null) => new()
        {
            Type = BiometricType.Finger,
            BioSubType = subTypes,
            Count = count,
            DeviceSubId = deviceSubId,
        };

    /// <summary>
    /// 一份人脸采集要求。
    /// </summary>
    private static RegistrationCaptureRequestBio Face(int? count = null) => new()
    {
        Type = BiometricType.Face,
        Count = count,
    };
}
