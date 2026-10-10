using System.Reflection;
using BiometricBridge.Core.Models;
using BiometricBridge.Core.Models.Enums;
using BiometricBridge.Device.Interop;
using Xunit;

namespace BiometricBridge.Device.Tests;

/// <summary>
/// <see cref="EyeIrisDevice"/> 的单元测试。
/// </summary>
/// <remarks>
/// <para>
/// 覆盖两类：一是<b>不碰原生库的"未连接"契约</b>；二是<b>本地调用之前的入参守卫</b>。
/// </para>
/// <para>
/// 后一类要求设备处于已连接态，而该状态只能由 <see cref="EyeIrisDevice.ConnectAsync"/> 走真机
/// <c>biospi_attach</c> 置上，故这里用反射把私有字段 <c>_attached</c> 置位 —— <b>只动这一个字段</b>，
/// 生产代码不受影响，也不必为此在设备层开口子。
/// </para>
/// <para>
/// <b>未覆盖</b>：<c>biospi_capture</c> 之后的逻辑（返回码解释、质量分→结果）。那段要把原生调用换掉
/// 才测得到，而设备类把 P/Invoke 直接焊在方法里（静态 extern，无注入点）；本工程虽把原生库拷到了
/// 输出目录，但没有真机时调用仍是未定义用法，故不在此断言其行为。
/// </para>
/// </remarks>
public class EyeIrisDeviceTests
{
    #region 未连接 —— 不碰原生库

    [Fact]
    public void 新建的设备未连接且无序列号()
    {
        var device = new EyeIrisDevice();

        Assert.False(device.IsConnected);
        Assert.Null(device.SerialNo);
    }

    [Fact]
    public async Task 未连接时采集抛设备未连接()
    {
        var device = new EyeIrisDevice();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => device.CaptureAsync(ValidIrisRequest(), TestContext.Current.CancellationToken));

        Assert.Contains("未连接", exception.Message);
    }

    [Fact]
    public async Task 未连接时起预览抛设备未连接()
    {
        var device = new EyeIrisDevice();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => device.RunPreviewAsync(_ => { }, TestContext.Current.CancellationToken));

        Assert.Contains("未连接", exception.Message);
    }

    [Fact]
    public async Task 起预览的接收端不得为null()
    {
        var device = new EyeIrisDevice();

        // 空值判定先于连接判定，故未连接也测得到。
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => device.RunPreviewAsync(null!, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task 未连接时断开是空操作()
    {
        var device = new EyeIrisDevice();

        // 不抛即通过：未连接时不应触碰原生 detach。
        await device.DisconnectAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task 未连接时释放是空操作()
    {
        var device = new EyeIrisDevice();

        // DisposeAsync 只是转调 DisconnectAsync，未连接时同样不该触碰原生层。
        await device.DisposeAsync();
    }

    #endregion

    #region 入参守卫 —— 需已连接态（反射置位）

    [Theory]
    [InlineData(BiometricModality.Finger)]
    [InlineData(BiometricModality.Face)]
    public async Task 非虹膜模态被拒(BiometricModality modality)
    {
        var device = Attached();

        await Assert.ThrowsAsync<NotSupportedException>(
            () => device.CaptureAsync(ValidIrisRequest() with { Modality = modality }, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task 超时非正被拒(int timeout)
    {
        var device = Attached();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => device.CaptureAsync(ValidIrisRequest() with { Timeout = timeout }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task 非虹膜位置被拒()
    {
        var device = Attached();

        await Assert.ThrowsAsync<ArgumentException>(
            () => device.CaptureAsync(
                ValidIrisRequest() with { Positions = [BiometricPosition.LeftThumb] },
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(101.0)]
    public async Task 质量分越界被拒(double score)
    {
        var device = Attached();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => device.CaptureAsync(
                ValidIrisRequest() with { RequestedScore = score },
                TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(100.0)]
    public async Task 合法入参越过守卫(double? score)
    {
        var device = Attached();

        var exception = await TryCaptureAsync(device, ValidIrisRequest() with { RequestedScore = score });

        // 守卫放行后必然落到原生调用，而本机没有真机可用，失败是必然的 —— 关键是不能是"入参被拒"的
        // 那几种异常：那说明守卫的界写错了（把合法值挡在外面）。
        Assert.NotNull(exception);
        Assert.False(
            exception is ArgumentException or NotSupportedException,
            $"合法入参本应越过守卫，实际却抛了入参类异常：{exception.GetType().Name}");
    }

    #endregion

    #region 眼睛选择 —— 纯映射，不碰原生库

    [Fact]
    public void 双眼点名取双眼标志()
        => Assert.Equal(
            EyeIrisPlatformApi.EyeBoth,
            EyeIrisDevice.ResolveEyes(IrisRequest(positions: [BiometricPosition.LeftIris, BiometricPosition.RightIris])));

    [Fact]
    public void 双眼点名与顺序无关()
        => Assert.Equal(
            EyeIrisPlatformApi.EyeBoth,
            EyeIrisDevice.ResolveEyes(IrisRequest(positions: [BiometricPosition.RightIris, BiometricPosition.LeftIris])));

    [Fact]
    public void 分组为双也取双眼标志()
        => Assert.Equal(
            EyeIrisPlatformApi.EyeBoth,
            EyeIrisDevice.ResolveEyes(IrisRequest(group: CaptureGroup.Both)));

    [Fact]
    public void 单眼点名按名取()
    {
        Assert.Equal(EyeIrisPlatformApi.EyeLeft, EyeIrisDevice.ResolveEyes(
            IrisRequest(positions: [BiometricPosition.LeftIris])));
        Assert.Equal(EyeIrisPlatformApi.EyeRight, EyeIrisDevice.ResolveEyes(
            IrisRequest(positions: [BiometricPosition.RightIris])));
    }

    [Fact]
    public void 只给分组时按组取()
    {
        Assert.Equal(EyeIrisPlatformApi.EyeLeft, EyeIrisDevice.ResolveEyes(IrisRequest(group: CaptureGroup.Left)));
        Assert.Equal(EyeIrisPlatformApi.EyeRight, EyeIrisDevice.ResolveEyes(IrisRequest(group: CaptureGroup.Right)));
    }

    [Fact]
    public void 都不给表示哪只都行()
        => Assert.Equal(
            EyeIrisPlatformApi.EyeEither,
            EyeIrisDevice.ResolveEyes(IrisRequest()));

    [Fact]
    public void 点名优先于分组()
        => Assert.Equal(
            EyeIrisPlatformApi.EyeLeft,
            EyeIrisDevice.ResolveEyes(IrisRequest(positions: [BiometricPosition.LeftIris], group: CaptureGroup.Both)));

    [Fact]
    public void 非虹膜位置解不出眼睛标志()
        => Assert.Throws<ArgumentException>(
            () => EyeIrisDevice.ResolveEyes(IrisRequest(positions: [BiometricPosition.LeftThumb])));

    [Fact]
    public void 多于两枚解不出眼睛标志()
        => Assert.Throws<ArgumentException>(
            () => EyeIrisDevice.ResolveEyes(IrisRequest(
                positions: [BiometricPosition.LeftIris, BiometricPosition.RightIris, BiometricPosition.LeftIris])));

    #endregion

    #region 帧判空 —— 纯函数，不碰原生库

    [Fact]
    public void 全零缓冲视为没有帧()
    {
        // 超时时库未必回填了帧，而全零是一个真实采集不可能出现的情形（画面总有底噪），
        // 故拿它当"没回填"的判据 —— 见 HasImageData 的备注。
        Assert.False(EyeIrisDevice.HasImageData(new byte[EyeIrisPlatformApi.IrisImageSize]));
    }

    [Fact]
    public void 有任何一个非零像素就视为有帧()
    {
        var image = new byte[EyeIrisPlatformApi.IrisImageSize];
        image[^1] = 1;

        Assert.True(EyeIrisDevice.HasImageData(image));
    }

    [Fact]
    public void 空缓冲视为没有帧()
        => Assert.False(EyeIrisDevice.HasImageData([]));

    #endregion

    #region 采集趟次 —— 纯函数，不碰原生库

    [Fact]
    public void 第二趟放宽门限并限时()
    {
        // 第一趟按请求的门限（达标即回，常规路径的耗时与从前一致）；第二趟把门限放宽到"来者不拒"，
        // 并给一个短上限，好让总耗时有个界。见 TwoAttempts 的备注。
        var attempts = EyeIrisDevice.TwoAttempts(Params(quality: 100, timeoutSeconds: 10), allowFallback: true)
            .ToArray();

        Assert.Equal(2, attempts.Length);
        Assert.Equal(100, attempts[0].Quality);
        Assert.Equal(10, attempts[0].TimeOut);

        Assert.Equal(0, attempts[1].Quality);
        Assert.Equal(3, attempts[1].TimeOut);
    }

    [Fact]
    public void 两趟之间只动门限与超时()
    {
        // 眼睛选择、曝光、触发方式、优先级、区域标志都必须照旧 —— 第二趟只是"门限放宽"，
        // 不是另一次形态不同的采集。
        var attempts = EyeIrisDevice.TwoAttempts(Params(quality: 40, timeoutSeconds: 10), allowFallback: true)
            .ToArray();

        Assert.Equal(attempts[0].Eyes, attempts[1].Eyes);
        Assert.Equal(attempts[0].EyeExpo, attempts[1].EyeExpo);
        Assert.Equal(attempts[0].Type, attempts[1].Type);
        Assert.Equal(attempts[0].Priority, attempts[1].Priority);
        Assert.Equal(attempts[0].Size, attempts[1].Size);
    }

    [Fact]
    public void 不许兜底时只跑一趟()
    {
        var attempts = EyeIrisDevice.TwoAttempts(Params(quality: 60, timeoutSeconds: 1), allowFallback: false)
            .ToArray();

        Assert.Single(attempts);
        Assert.Equal(60, attempts[0].Quality);
    }

    [Theory]
    [InlineData(500, false)]    // CTK 的"无输入超时"用例：要的是采集类错误
    [InlineData(1000, false)]
    [InlineData(2999, false)]
    [InlineData(3000, true)]    // 预算刚好等于一次兜底采集
    [InlineData(10000, true)]   // CTK 的"有输入超时"用例：要的是成功的最佳帧
    public void 兜底只在预算够长时才跑(int requestTimeoutMs, bool expected)
    {
        // 这条分界正是"有输入超时"与"无输入超时"两批用例的分野，判错方向会把一边的好用例
        // 变成另一边的坏用例。
        Assert.Equal(expected, EyeIrisDevice.AllowsFallback(requestTimeoutMs));
    }

    #endregion

    #region 辅助

    /// <summary>
    /// 一份采集参数，只指定本组测试关心的两项；其余取注册档位的合法值。
    /// </summary>
    /// <param name="quality">
    /// 质量门限。
    /// </param>
    /// <param name="timeoutSeconds">
    /// 超时（秒）。
    /// </param>
    private static EyeIrisPlatformApi.BioSpiParams Params(int quality, int timeoutSeconds) => new()
    {
        Eyes = EyeIrisPlatformApi.EyeBoth,
        Quality = quality,
        EyeExpo = 70,
        TimeOut = timeoutSeconds,
        Type = EyeIrisPlatformApi.CaptureTypeAuto,
        Priority = EyeIrisPlatformApi.PrioritySpeed,
        Size = 0x00000303,
    };

    /// <summary>
    /// 一份各字段都合法的虹膜采集请求。
    /// </summary>
    private static CaptureRequest ValidIrisRequest() => new()
    {
        Modality = BiometricModality.Iris,
        Positions = [BiometricPosition.LeftIris],
        Timeout = 1000,
    };

    /// <summary>
    /// 一份只关心"要采哪只眼"的虹膜请求。
    /// </summary>
    /// <param name="positions">
    /// 点名的部位；null 表示不点名
    /// </param>
    /// <param name="group">
    /// 分组
    /// </param>
    private static CaptureRequest IrisRequest(
        IReadOnlyList<BiometricPosition>? positions = null,
        CaptureGroup group = CaptureGroup.Any) => new()
        {
            Modality = BiometricModality.Iris,
            Positions = positions ?? [],
            Group = group,
            Timeout = 1000,
        };

    /// <summary>
    /// 造一台"已连接"的设备：反射置位私有字段 <c>_attached</c>。
    /// </summary>
    /// <remarks>
    /// 只置这一个字段。不调 <see cref="EyeIrisDevice.DisconnectAsync"/>/<see cref="EyeIrisDevice.DisposeAsync"/>，
    /// 因为这时的"已连接"是假的（原生侧并无连接），真去 detach 就是拿假状态调原生。
    /// </remarks>
    private static EyeIrisDevice Attached()
    {
        var device = new EyeIrisDevice();
        AttachedField.SetValue(device, true);
        return device;
    }

    /// <summary>
    /// 私有字段 <c>_attached</c>。
    /// </summary>
    private static readonly FieldInfo AttachedField = typeof(EyeIrisDevice)
        .GetField("_attached", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("EyeIrisDevice._attached 字段未找到：类结构变了，测试需要同步。");

    /// <summary>
    /// 采集并把异常原样接住（含与 <c>Assert.ThrowsAsync</c> 不同的"可能不抛"的情形）。
    /// </summary>
    private static async Task<Exception?> TryCaptureAsync(EyeIrisDevice device, CaptureRequest request)
    {
        try
        {
            await device.CaptureAsync(request, TestContext.Current.CancellationToken);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    #endregion
}
