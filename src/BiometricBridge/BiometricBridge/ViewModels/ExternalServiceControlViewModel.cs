using System;
using System.ComponentModel;
using System.Threading.Tasks;
using BiometricBridge.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;

namespace BiometricBridge.ViewModels;

/// <summary>
/// 对外服务控制面板视图模型：启停对外服务宿主，并承载与宿主类型相关的设置。
/// </summary>
/// <remarks>
/// <para>
/// 对外服务的启停归本类，配置（HTTP 的端口）归 <see cref="Settings"/>。设置单独成一个子视图模型、
/// 由视图用 <c>ContentControl</c> 托出，是为了将来换宿主类型（如 Android Intent，参数不同）时
/// 只替换那一块，下面这套公共的启停逻辑不动 —— 现阶段只有 HTTP 一个宿主，故不预先抽出基类。
/// </para>
/// <para>
/// <b>运行状态由本类自持</b>：宿主契约刻意不暴露"在不在跑"（那是实现自己的事），而本面板是唯一的
/// 启停发起方，故如实镜像即可。注意这是约定而非结构保证 —— <see cref="App"/> 退出时兜底调的那次 Stop
/// 不经过本类，但那一刻进程本就在收尾，状态错位无影响。若将来出现第二个启停发起方，该状态应改由宿主
/// 以事件广播（同设备管理器之于预览）。
/// </para>
/// </remarks>
public partial class ExternalServiceControlViewModel : ObservableObject
{
    #region Fileds

    private readonly IExternalServiceHost _host;

    #endregion

    /// <summary>
    /// 构造视图模型。
    /// </summary>
    /// <param name="host">
    /// 对外服务宿主；启停都经它。
    /// </param>
    /// <param name="settings">
    /// 与宿主类型相关的设置子视图模型。
    /// </param>
    public ExternalServiceControlViewModel(IExternalServiceHost host, HttpHostSettingsViewModel settings)
    {
        _host = host;
        Settings = settings;

        // 端口合法与否决定"启动"能否执行，故设置一变就重估。
        Settings.PropertyChanged += OnSettingsChanged;
    }

    #region ObservableProperties

    /// <summary>
    /// 对外服务是否在跑。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleButtonText))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isRunning;

    #endregion

    #region Properties

    /// <summary>
    /// 与宿主类型相关的设置；只读，由视图用模板托出。
    /// </summary>
    public HttpHostSettingsViewModel Settings { get; }

    /// <summary>
    /// 启停按钮的文案。
    /// </summary>
    public string ToggleButtonText => IsRunning ? "停止服务" : "启动服务";

    /// <summary>
    /// 运行状态文案。
    /// </summary>
    public string StatusText => IsRunning ? "运行中" : "已停止";

    #endregion

    #region Commands

    /// <summary>
    /// 开/停对外服务。
    /// </summary>
    /// <returns>
    /// 表示异步操作的任务。
    /// </returns>
    [RelayCommand(CanExecute = nameof(CanToggle))]
    private async Task ToggleAsync()
    {
        if (IsRunning)
        {
            try
            {
                await _host.StopAsync().ConfigureAwait(true);
            }
            catch (Exception exception)
            {
                // 停不下来已无从补救，留痕后按已停收拾 —— 面板不该卡在一个它已放手的服务上。
                Log.Error(exception, "停止对外服务失败。");
            }

            IsRunning = false;
            return;
        }

        try
        {
            await _host.StartAsync().ConfigureAwait(true);

            // 只有真起来了才置位。
            IsRunning = true;
        }
        catch (Exception exception)
        {
            // 起不来（如端口被占）：留痕，状态保持"已停止"。此处没有设备装饰器那样的统一记录点，故显式记。
            Log.Error(exception, "启动对外服务失败。");
        }
    }

    /// <summary>
    /// 启停可否执行：停止随时可以；启动要求端口合法。
    /// </summary>
    /// <returns>
    /// 可执行返回 true。
    /// </returns>
    private bool CanToggle() => IsRunning || Settings.IsValid;

    #endregion

    /// <summary>
    /// 设置变化：重估启停命令的可执行性。
    /// </summary>
    /// <param name="sender">
    /// 事件源。
    /// </param>
    /// <param name="e">
    /// 事件参数。
    /// </param>
    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
    {
        ToggleCommand.NotifyCanExecuteChanged();
    }
}
