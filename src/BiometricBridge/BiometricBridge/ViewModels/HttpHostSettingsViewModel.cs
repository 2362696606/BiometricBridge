using BiometricBridge.Host.Http;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiometricBridge.ViewModels;

/// <summary>
/// HTTP 宿主的设置视图模型：编辑监听端口，并写回与宿主共享的设置对象。
/// </summary>
/// <remarks>
/// <para>
/// 端口是宿主类型相关的设置，独立成类，供控制面板按宿主类型替换（见
/// <see cref="ExternalServiceControlViewModel"/> 的说明）。
/// </para>
/// <para>
/// <b>回写式</b>：端口一改且合法就写进 <see cref="HttpHostSettings"/>，宿主下次启动读到的即是新值，
/// 故本类不需暴露"应用"动作。运行中改端口不即时生效 —— 视图在运行期禁用输入，避免误解。
/// </para>
/// <para>
/// 允许的范围取自 <see cref="HttpHostSettings"/> 的常量（SBI 约定的一段），校验、输入控件边界、
/// 提示语三处都引这一处，免得范围改动后各写各的、悄悄漂移。
/// </para>
/// </remarks>
public partial class HttpHostSettingsViewModel : ObservableObject
{
    private readonly HttpHostSettings _settings;

    /// <summary>
    /// 构造视图模型。
    /// </summary>
    /// <param name="settings">
    /// 与宿主共享的设置对象。
    /// </param>
    public HttpHostSettingsViewModel(HttpHostSettings settings)
    {
        _settings = settings;

        // 以共享设置里的当前值为准，而非另起默认值：面板与宿主看到的是同一个端口。
        Port = settings.Port;
    }

    #region ObservableProperties

    /// <summary>
    /// 监听端口。
    /// </summary>
    /// <remarks>
    /// 类型取 <c>decimal?</c> 而非 <c>int?</c>：绑定目标是 <c>NumericUpDown.Value</c>，其类型就是
    /// <c>decimal?</c>，同类型绑定免去转换器，也顺带让空值（未填）自然表达为 null。
    /// </remarks>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsValid))]
    private decimal? _port;

    #endregion

    #region Properties

    /// <summary>
    /// 端口是否合法。"启动"能否执行看它。
    /// </summary>
    public bool IsValid => Port is >= HttpHostSettings.MinPort and <= HttpHostSettings.MaxPort;

    /// <summary>
    /// 允许的端口下限，供输入控件设界。
    /// </summary>
    public decimal MinPort => HttpHostSettings.MinPort;

    /// <summary>
    /// 允许的端口上限，供输入控件设界。
    /// </summary>
    public decimal MaxPort => HttpHostSettings.MaxPort;

    /// <summary>
    /// 端口非法时的提示语。
    /// </summary>
    /// <remarks>
    /// 由常量拼出而非在视图里写死，免得范围改动后提示语忘了改。
    /// </remarks>
    public string PortRangeHint => $"端口须在 {HttpHostSettings.MinPort}–{HttpHostSettings.MaxPort} 之间";

    #endregion

    /// <summary>
    /// 端口变化：合法就写回共享设置。
    /// </summary>
    /// <param name="value">
    /// 新端口。
    /// </param>
    /// <remarks>
    /// 不合法则不写：宿主读到的仍是上一次的合法值，不会拿到越界端口。
    /// </remarks>
    partial void OnPortChanged(decimal? value)
    {
        if (value is >= HttpHostSettings.MinPort and <= HttpHostSettings.MaxPort)
        {
            _settings.Port = (int)value;
        }
    }
}
