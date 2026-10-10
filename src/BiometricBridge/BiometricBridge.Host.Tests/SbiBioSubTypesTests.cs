using BiometricBridge.Core.Models.Enums;
using BiometricBridge.Host;
using BiometricBridge.Host.Iso;
using JetBrains.Annotations;
using Xunit;

namespace BiometricBridge.Host.Tests;

/// <summary>
/// <see cref="SbiBioSubTypes"/> 的一致性测试
/// </summary>
/// <remarks>
/// 这层测试守的是"多张表不许漂移"：SBI 字面量表、分组表、以及 ISO 位置码表分处不同文件，
/// 新增一个采集位置却漏补其中一张，线上表现是"发出去的字面量变成 UNKNOWN、ISO 位置码变成 0"
/// 这类静默错误 —— 靠断言当场拦住。
/// </remarks>
[TestSubject(typeof(SbiBioSubTypes))]
public class SbiBioSubTypesTests
{
    /// <summary>
    /// 规范给的具名部位：指纹十枚 + 虹膜两枚（不含 UNKNOWN）。
    /// </summary>
    private const int NamedPositionCount = 12;

    [Fact]
    public void 具名部位的数量与规范一致()
        => Assert.Equal(NamedPositionCount, SbiBioSubTypes.NamedPositions.Count);

    [Fact]
    public void 具名部位与字面量能往返()
    {
        foreach (var position in SbiBioSubTypes.NamedPositions)
        {
            var modality = ModalityOf(position);
            var literal = SbiBioSubTypes.LiteralOf(position);

            Assert.NotEqual(SbiBioSubTypes.UnknownLiteral, literal);
            Assert.True(
                SbiBioSubTypes.TryParseLiteral(literal, modality, out var parsed),
                $"{position} 的字面量 {literal} 解析不回来。");
            Assert.Equal(position, parsed);
        }
    }

    [Fact]
    public void 判不出部位发UNKNOWN()
    {
        Assert.Equal(SbiBioSubTypes.UnknownLiteral, SbiBioSubTypes.LiteralOf(null));
        Assert.Equal(SbiBioSubTypes.UnknownLiteral, SbiBioSubTypes.LiteralOf(BiometricPosition.Unknown));
    }

    [Fact]
    public void UNKNOWN是不具名的合法值()
    {
        // 合法（请求里表示"不点名"）但解不出具名部位 —— 与"非法字面量"要分得开，才好决定报不报错。
        Assert.True(SbiBioSubTypes.IsUnknownLiteral(SbiBioSubTypes.UnknownLiteral));
        Assert.False(SbiBioSubTypes.IsUnknownLiteral("Left"));
        Assert.False(SbiBioSubTypes.IsUnknownLiteral(null));

        Assert.False(SbiBioSubTypes.TryParseLiteral(SbiBioSubTypes.UnknownLiteral, BiometricModality.Iris, out _));
    }

    [Fact]
    public void 字面量大小写严格()
    {
        Assert.False(SbiBioSubTypes.TryParseLiteral("left", BiometricModality.Iris, out _));
        Assert.False(SbiBioSubTypes.TryParseLiteral("Left indexFinger", BiometricModality.Finger, out _));
        Assert.False(SbiBioSubTypes.TryParseLiteral("Left", BiometricModality.Finger, out _));
        Assert.False(SbiBioSubTypes.TryParseLiteral("Left IndexFinger", BiometricModality.Iris, out _));
        Assert.False(SbiBioSubTypes.TryParseLiteral(null, BiometricModality.Iris, out _));
    }

    [Fact]
    public void 每个具名部位都落在一个分组里()
    {
        foreach (var position in SbiBioSubTypes.NamedPositions)
        {
            var modality = ModalityOf(position);

            Assert.True(
                SbiBioSubTypes.TryGroupOf(modality, [position], out var group),
                $"{position} 推不出分组。");
            Assert.True(
                SbiBioSubTypes.IsInGroup(position, modality, group),
                $"{position} 反推的 {group} 组却不含它。");
        }
    }

    [Fact]
    public void 左右四指不含拇指()
    {
        var left = SbiBioSubTypes.PositionsOfGroup(BiometricModality.Finger, CaptureGroup.Left);

        Assert.Equal(4, left.Count);
        Assert.DoesNotContain(BiometricPosition.LeftThumb, left);
        Assert.False(SbiBioSubTypes.IsInGroup(BiometricPosition.LeftThumb, BiometricModality.Finger, CaptureGroup.Left));
    }

    [Fact]
    public void 双拇指自成第三组()
        => Assert.Equal(
            [BiometricPosition.LeftThumb, BiometricPosition.RightThumb],
            SbiBioSubTypes.PositionsOfGroup(BiometricModality.Finger, CaptureGroup.Both));

    [Fact]
    public void 双眼是虹膜的第三组()
        => Assert.Equal(
            [BiometricPosition.LeftIris, BiometricPosition.RightIris],
            SbiBioSubTypes.PositionsOfGroup(BiometricModality.Iris, CaptureGroup.Both));

    [Fact]
    public void 双眼请求推出来的是双组而单眼推出来的是单侧组()
    {
        Assert.True(SbiBioSubTypes.TryGroupOf(
            BiometricModality.Iris,
            [BiometricPosition.LeftIris, BiometricPosition.RightIris],
            out var both));
        Assert.Equal(CaptureGroup.Both, both);

        // 只点名左眼：它同时"属于"左组与双组，须取最具体的那个，否则会把单眼请求误当双眼。
        Assert.True(SbiBioSubTypes.TryGroupOf(BiometricModality.Iris, [BiometricPosition.LeftIris], out var left));
        Assert.Equal(CaptureGroup.Left, left);
    }

    [Fact]
    public void 非本模态的部位不属于该模态任何组()
    {
        Assert.False(SbiBioSubTypes.IsInGroup(BiometricPosition.LeftThumb, BiometricModality.Iris, CaptureGroup.Any));
        Assert.False(SbiBioSubTypes.IsInGroup(BiometricPosition.LeftIris, BiometricModality.Finger, CaptureGroup.Any));
        Assert.Empty(SbiBioSubTypes.PositionsOfGroup(BiometricModality.Face, CaptureGroup.Any));
    }

    [Fact]
    public void 跨组的一组部位推不出分组()
    {
        // 左食指属"左四指"、左拇指属"双拇指"：凑在一起不属于任何一组。
        Assert.False(SbiBioSubTypes.TryGroupOf(
            BiometricModality.Finger,
            [BiometricPosition.LeftIndexFinger, BiometricPosition.LeftThumb],
            out _));
    }

    [Fact]
    public void 空集合推不出分组()
        => Assert.False(SbiBioSubTypes.TryGroupOf(BiometricModality.Iris, [], out _));

    [Fact]
    public void deviceSubId与分组双向对应()
    {
        foreach (var group in (CaptureGroup[])[CaptureGroup.Any, CaptureGroup.Left, CaptureGroup.Right, CaptureGroup.Both])
        {
            var subId = SbiBioSubTypes.DeviceSubIdOf(group);

            Assert.True(SbiBioSubTypes.TryGroupOfDeviceSubId(subId, out var back));
            Assert.Equal(group, back);
        }
    }

    [Fact]
    public void deviceSubId越界被拒()
    {
        Assert.False(SbiBioSubTypes.TryGroupOfDeviceSubId(-1, out _));
        Assert.False(SbiBioSubTypes.TryGroupOfDeviceSubId(4, out _));
    }

    [Fact]
    public void 每个具名部位都有ISO位置码()
    {
        foreach (var position in SbiBioSubTypes.NamedPositions)
        {
            var code = ModalityOf(position) == BiometricModality.Iris
                ? Iso19794IrisImage.EyeLabel(position)
                : Iso19794FingerImage.PositionCode(position);

            Assert.True(code != 0, $"{position} 没有 ISO 编码（会静默编成 0）—— ISO 编码表需要同步补上。");
        }

        // 兜底取值仍应是 ISO 的"未知"：判不出部位时不能瞎报一个具体位置。
        Assert.Equal(0, Iso19794FingerImage.PositionCode(BiometricPosition.Unknown));
        Assert.Equal(0, Iso19794IrisImage.EyeLabel(null));
        Assert.Equal(0, Iso19794IrisImage.EyeLabel(BiometricPosition.LeftThumb));
    }

    /// <summary>
    /// 位置所属的模态。
    /// </summary>
    private static BiometricModality ModalityOf(BiometricPosition position)
        => position is BiometricPosition.LeftIris or BiometricPosition.RightIris
            ? BiometricModality.Iris
            : BiometricModality.Finger;
}
