using BiometricBridge.Core.Models;
using BiometricBridge.Core.Models.Enums;
using JetBrains.Annotations;
using Xunit;

namespace BiometricBridge.Device.Tests;

/// <summary>
/// <see cref="FingerLabel"/> 与 <see cref="FCK3L.ExpectedFingerCount"/> 的单元测试
/// </summary>
/// <remarks>
/// 两者都是纯映射／纯计算，不碰原生库，故可脱离真机直接断言。它们决定的是<b>响应里的部位</b>，
/// 推错了会把"左手食指"报成"右手无名指"这种静默错误发出去 —— 值得钉住。
/// </remarks>
[TestSubject(typeof(FingerLabel))]
public class FingerLabelTests
{
    [Theory]
    [InlineData(0, BiometricPosition.LeftIndexFinger)]
    [InlineData(1, BiometricPosition.LeftMiddleFinger)]
    [InlineData(2, BiometricPosition.LeftRingFinger)]
    [InlineData(3, BiometricPosition.LeftLittleFinger)]
    public void 左手按分割序号推四指(int index, BiometricPosition expected)
        => Assert.Equal(expected, FingerLabel.Of(index, FingerHand.Left, CaptureGroup.Any));

    [Theory]
    [InlineData(0, BiometricPosition.RightIndexFinger)]
    [InlineData(1, BiometricPosition.RightMiddleFinger)]
    [InlineData(2, BiometricPosition.RightRingFinger)]
    [InlineData(3, BiometricPosition.RightLittleFinger)]
    public void 右手按分割序号推四指(int index, BiometricPosition expected)
        => Assert.Equal(expected, FingerLabel.Of(index, FingerHand.Right, CaptureGroup.Any));

    [Theory]
    [InlineData(CaptureGroup.Left, BiometricPosition.LeftIndexFinger)]
    [InlineData(CaptureGroup.Right, BiometricPosition.RightIndexFinger)]
    public void 分组指定的左右压过分割器判出的手别(CaptureGroup group, BiometricPosition expected)
    {
        // 分割器说右手、请求只认左手：按请求侧分组推，免得报出一个请求里没有的部位。
        Assert.Equal(expected, FingerLabel.Of(0, FingerHand.Right, group));
        Assert.Equal(expected, FingerLabel.Of(0, FingerHand.Left, group));

        // 手别判不出也一样：分组本身就说了是哪只手，用不着它。
        Assert.Equal(expected, FingerLabel.Of(0, FingerHand.Unknown, group));
    }

    [Fact]
    public void 判不出手别且未指定分组时推不出部位()
        => Assert.Equal(BiometricPosition.Unknown, FingerLabel.Of(0, FingerHand.Unknown, CaptureGroup.Any));

    [Fact]
    public void 双拇指按左先右后配对()
    {
        // 双拇指跨左右手，手别出参在这一组上没有意义，故按约定配对而不是看 hand。
        Assert.Equal(BiometricPosition.LeftThumb, FingerLabel.Of(0, FingerHand.Unknown, CaptureGroup.Both));
        Assert.Equal(BiometricPosition.RightThumb, FingerLabel.Of(1, FingerHand.Unknown, CaptureGroup.Both));
    }

    [Fact]
    public void 序号越界推不出部位()
    {
        Assert.Equal(BiometricPosition.Unknown, FingerLabel.Of(4, FingerHand.Left, CaptureGroup.Any));
        Assert.Equal(BiometricPosition.Unknown, FingerLabel.Of(2, FingerHand.Left, CaptureGroup.Both));
        Assert.Equal(BiometricPosition.Unknown, FingerLabel.Of(-1, FingerHand.Left, CaptureGroup.Any));
    }

    [Fact]
    public void 期望手指数由分组与枚数定()
    {
        Assert.Equal(4, FCK3L.ExpectedFingerCount(Request(CaptureGroup.Left)));
        Assert.Equal(4, FCK3L.ExpectedFingerCount(Request(CaptureGroup.Right)));
        Assert.Equal(2, FCK3L.ExpectedFingerCount(Request(CaptureGroup.Both)));

        // 未指定分组时按请求的枚数；枚数也没给，就按本传感器的常规联采枚数。
        Assert.Equal(3, FCK3L.ExpectedFingerCount(Request(CaptureGroup.Any, count: 3)));
        Assert.Equal(4, FCK3L.ExpectedFingerCount(Request(CaptureGroup.Any)));
    }

    /// <summary>
    /// 一份指纹采集请求。
    /// </summary>
    /// <param name="group">
    /// 分组
    /// </param>
    /// <param name="count">
    /// 枚数
    /// </param>
    private static CaptureRequest Request(CaptureGroup group, int count = 0) => new()
    {
        Modality = BiometricModality.Finger,
        Group = group,
        Count = count,
        Timeout = 1000,
    };
}
