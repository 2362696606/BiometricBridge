using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using BiometricBridge.Common;
using BiometricBridge.Core.Models;
using BiometricBridge.Core.Models.Enums;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BiometricBridge.ViewModels;

/// <summary>
/// 预览视图模型：驱动选中设备的预览流，并把投来的帧呈现到界面。
/// </summary>
/// <remarks>
/// <para>
/// 职责分两层：**何时开/停预览**归本类（用户点开关、换设备、断开），
/// **预览状态的真相**归 <see cref="BiometricDeviceManager"/> —— 本类只镜像它广播的状态，不自行推定。
/// 预览期间设备被占用，其它设备操作会把流顶掉，这种"非本类发起的停止"只有管理器知道。
/// </para>
/// <para>
/// 帧不由设备直接投给本类，而是经 <see cref="PreviewFrameStream"/> 转一手：帧率下限由设备装饰链保证
/// （见 <c>PacingDeviceDecorator</c>），线程切换与"只留最新一帧"由帧流做掉。于是本类拿到的就是
/// <b>界面线程上的、当下的</b>那一帧，只管画。
/// </para>
/// <para>
/// 订阅而不退订：本 VM 与帧流、管理器、选中服务同为进程级寿命（壳一次性构建 VM），一起随进程结束，
/// 不构成泄漏。若将来本视图可重建，退订应放在视图的 <c>DetachedFromVisualTree</c> —— 与
/// <see cref="LogViewModel"/> 同一考量。
/// </para>
/// </remarks>
public partial class PreviewViewModel : ObservableObject
{
    #region Fileds

    private readonly BiometricDeviceManager _deviceManager;

    private readonly PreviewFrameStream _stream;

    private readonly DeviceSelectionService _selection;

    /// <summary>
    /// 当前预览所属的设备 id；null 表示本类认为没有预览在跑。
    /// </summary>
    /// <remarks>
    /// 由管理器广播的状态驱动（见 <see cref="OnPreviewStateChanged"/>）。存的是 id 而不是选中的设备，
    /// 因为换设备时"要停的是旧那台"，而那一刻选中项已经变了。
    /// </remarks>
    private Guid? _previewDeviceId;

    /// <summary>
    /// 选中设备是否已连接。自持一份，理由同 <see cref="DeviceControlViewModel"/>：管理器事件与列表刷新
    /// 是两条独立的 Post，直接读列表项可能拿到旧值；权威来源是事件里的快照。
    /// </summary>
    private bool _isSelectedConnected;

    #endregion

    /// <summary>
    /// 构造视图模型。
    /// </summary>
    /// <param name="deviceManager">
    /// 设备管理器，预览的启停都经它转发。
    /// </param>
    /// <param name="stream">
    /// 预览帧流；既是交给设备的那只接收端，也是本类帧的来源。
    /// </param>
    /// <param name="selection">
    /// 共享的设备选中服务。
    /// </param>
    public PreviewViewModel(
        BiometricDeviceManager deviceManager,
        PreviewFrameStream stream,
        DeviceSelectionService selection)
    {
        _deviceManager = deviceManager;
        _stream = stream;
        _selection = selection;

        _stream.FrameAvailable += OnFrameAvailable;
        _deviceManager.DeviceStateChanged += OnDeviceStateChanged;
        _deviceManager.PreviewStateChanged += OnPreviewStateChanged;
        _selection.PropertyChanged += OnSelectionChanged;

        SeedFromSelection();
    }

    #region ObservableProperties

    /// <summary>
    /// 是否正在预览。镜像管理器广播的状态。
    /// </summary>
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PreviewButtonText))]
    private bool _isPreviewing;

    #endregion

    #region Properties

    /// <summary>
    /// 当前选中的设备；只读。预览面板无权改变选择，故这里只做代理。
    /// </summary>
    public DeviceInfoItemViewModel? SelectedDevice => _selection.SelectedDeviceItem;

    /// <summary>
    /// 预览开关的文案。
    /// </summary>
    public string PreviewButtonText => IsPreviewing ? "停止预览" : "开始预览";

    /// <summary>
    /// 预览画面。每个出现过的采集位置一格 —— 虹膜左右眼两格，单帧设备一格。
    /// </summary>
    /// <remarks>
    /// 做成集合而不写死两个属性：格子数由设备实际投出的位置决定，视图无需按设备类型分支；
    /// 中途增减（例如左眼先到、右眼后到）也能自然呈现。
    /// </remarks>
    public ObservableCollection<PreviewPaneViewModel> Panes { get; } = [];

    #endregion

    #region Commands

    /// <summary>
    /// 开/关预览。
    /// </summary>
    /// <returns>
    /// 表示异步操作的任务。
    /// </returns>
    [RelayCommand(CanExecute = nameof(CanTogglePreview))]
    private async Task TogglePreviewAsync()
    {
        if (_previewDeviceId is not null)
        {
            await StopPreviewAsync().ConfigureAwait(true);
            return;
        }

        if (SelectedDevice is not { } item)
        {
            return;
        }

        try
        {
            // 状态不在这里置位：管理器装好会话后会广播"开始"，由那里统一改。
            await _deviceManager.StartPreviewAsync(item.DeviceId, _stream.OnFrame);
        }
        catch (Exception)
        {
            // 起流失败（未连接等）由日志装饰器记录；没装成会话就不会有广播，状态自然仍是"未预览"。
        }
    }

    /// <summary>
    /// 开关是否可执行：停止随时可以；开始要求选中且已连接。
    /// </summary>
    /// <returns>
    /// 可执行返回 true。
    /// </returns>
    private bool CanTogglePreview()
    {
        return _previewDeviceId is not null || (SelectedDevice is not null && _isSelectedConnected);
    }

    #endregion

    /// <summary>
    /// 有一帧可用：画到它所属的格子上。
    /// </summary>
    /// <param name="sender">
    /// 事件源。
    /// </param>
    /// <param name="frame">
    /// 预览帧。
    /// </param>
    /// <remarks>
    /// 帧流已在界面线程上触发本事件，故这里不必再切线程，只管画。
    /// </remarks>
    private void OnFrameAvailable(object? sender, PreviewFrame frame)
    {
        Render(frame);
    }

    /// <summary>
    /// 预览状态变化：只关心当前选中那台。
    /// </summary>
    /// <param name="sender">
    /// 事件源。</param>
    /// <param name="e">
    /// 事件参数。
    /// </param>
    /// <remarks>
    /// 只捕获不可变的值，不捕获事件参数对象：Post 会把闭包留到调度器执行它为止。
    /// </remarks>
    private void OnPreviewStateChanged(object? sender, PreviewStateChangedEventArgs e)
    {
        var deviceId = e.DeviceId;
        var isPreviewing = e.IsPreviewing;

        Dispatcher.UIThread.Post(() =>
        {
            // 只认选中设备的：别的设备上报的启停与本面板无关。
            if (deviceId != SelectedDevice?.DeviceId)
            {
                return;
            }

            _previewDeviceId = isPreviewing ? deviceId : null;
            IsPreviewing = isPreviewing;

            if (isPreviewing)
            {
                _stream.Start();
            }
            else
            {
                // 流已停，画面若还留着最后一帧，看起来就像"冻住但还活着"——必须清掉。
                _stream.Stop();
                Panes.Clear();
            }

            RefreshCommands();
        });
    }

    /// <summary>
    /// 设备状态变化：只更新"选中设备是否已连接"，它决定开关能否执行。
    /// </summary>
    /// <param name="sender">
    /// 事件源。
    /// </param>
    /// <param name="e">
    /// 事件参数。
    /// </param>
    private void OnDeviceStateChanged(object? sender, DeviceStateChangedEventArgs e)
    {
        var snapshot = e.Snapshot;

        Dispatcher.UIThread.Post(() =>
        {
            if (snapshot.DeviceId != SelectedDevice?.DeviceId)
            {
                return;
            }

            _isSelectedConnected = snapshot.IsConnected;
            RefreshCommands();
        });
    }

    /// <summary>
    /// 选中项变化：停掉旧设备的预览，再对齐到新设备。
    /// </summary>
    /// <param name="sender">
    /// 事件源。
    /// </param>
    /// <param name="e">
    /// 事件参数。
    /// </param>
    /// <remarks>
    /// 必须换手：预览占着设备，不换就成了"看着 A 的画面、选中的却是 B"。
    /// </remarks>
    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DeviceSelectionService.SelectedDeviceItem))
        {
            return;
        }

        // 不 await：选择变更不该被停止预览的往返拖住。停止过程内部的异常它自己已收口。
        _ = StopPreviewAsync();

        SeedFromSelection();
    }

    /// <summary>
    /// 停掉当前预览（若有），并把本类状态按"已停"收拾好。
    /// </summary>
    /// <returns>
    /// 表示异步操作的任务。
    /// </returns>
    /// <remarks>
    /// 状态先落、再等设备侧停完：主动停止不会触发管理器的广播，所以这边得自己收拾干净，
    /// 否则开关会卡在"预览中"而实际已经停了。
    /// </remarks>
    private async Task StopPreviewAsync()
    {
        if (_previewDeviceId is not { } deviceId)
        {
            return;
        }

        _previewDeviceId = null;
        IsPreviewing = false;

        // 先叫停再等设备侧停完：等待期间在飞的帧若照收，刚清空的画面会被它们重新填回来。
        _stream.Stop();
        Panes.Clear();
        RefreshCommands();

        try
        {
            await _deviceManager.StopPreviewAsync(deviceId);
        }
        catch (Exception)
        {
            // 停不下来（设备侧宽限内没收到结束回调）已由管理器与日志装饰器呈现；
            // 状态这边已经按"已停"收拾好，不再回滚 —— 面板不该卡在一个它已经放手的流上。
        }
    }

    /// <summary>
    /// 把面板对齐到当前选中设备的初始状态。
    /// </summary>
    private void SeedFromSelection()
    {
        _isSelectedConnected = SelectedDevice?.IsConnected ?? false;
        RefreshCommands();
    }

    /// <summary>
    /// 重新求值开关的可执行性。
    /// </summary>
    private void RefreshCommands()
    {
        TogglePreviewCommand.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// 把一帧画到它所属的格子上。
    /// </summary>
    /// <param name="frame">
    /// 预览帧。
    /// </param>
    private void Render(PreviewFrame frame)
    {
        // 数据长度与几何不符说明上游给了坏帧，宁可留白也不要让 Marshal 越界。
        if (frame.Data.Length < frame.Width * frame.Height)
        {
            return;
        }

        GetOrCreatePane(frame.Position).Image = CreateBitmap(frame);
    }

    /// <summary>
    /// 取该位置对应的格子，没有就建一个。
    /// </summary>
    /// <param name="position">
    /// 采集位置；null 表示设备未指明。
    /// </param>
    /// <returns>
    /// 该位置的格子。
    /// </returns>
    /// <remarks>
    /// 按位置在枚举里的次序插入，而不是按到达顺序追加：左右眼谁先到取决于设备，界面不该跟着抖。
    /// </remarks>
    private PreviewPaneViewModel GetOrCreatePane(BiometricPosition? position)
    {
        foreach (var pane in Panes)
        {
            if (pane.Position == position)
            {
                return pane;
            }
        }

        var created = new PreviewPaneViewModel(position);
        var rank = Rank(position);

        var index = 0;
        while (index < Panes.Count && Rank(Panes[index].Position) <= rank)
        {
            index++;
        }

        Panes.Insert(index, created);
        return created;
    }

    /// <summary>
    /// 排序权重：null 与 <see cref="BiometricPosition.Unknown"/> 同等（都是"说不清位置"）。
    /// </summary>
    /// <param name="position">
    /// 采集位置。
    /// </param>
    /// <returns>
    /// 用于排列格子的权重。
    /// </returns>
    private static int Rank(BiometricPosition? position) => (int)(position ?? BiometricPosition.Unknown);

    /// <summary>
    /// 把一帧拷成一个新位图。
    /// </summary>
    /// <param name="frame">
    /// 预览帧。
    /// </param>
    /// <returns>
    /// 位图。
    /// </returns>
    /// <remarks>
    /// <b>每帧新建位图实例。</b>Avalonia 12.1.3 的 <c>IImage</c> 只暴露 <c>Draw</c> 与 <c>Size</c>、
    /// 没有失效通知，原地改写同一张位图不会让 <c>Image</c> 重绘；换实例是简单且必然触发重绘的路子。
    /// 若日后实测分配压力大，再考虑复用缓冲 + 视图侧手动失效。
    /// </remarks>
    private static WriteableBitmap CreateBitmap(PreviewFrame frame)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(frame.Width, frame.Height),
            new Vector(96, 96),
            PixelFormats.Gray8,
            AlphaFormat.Opaque);

        using (var buffer = bitmap.Lock())
        {
            // 逐行拷：Lock 给出的 RowBytes 可能大于 Width（行按对齐补过），整块拷贝会错行。
            for (var y = 0; y < frame.Height; y++)
            {
                Marshal.Copy(frame.Data, y * frame.Width, buffer.Address + y * buffer.RowBytes, frame.Width);
            }
        }

        return bitmap;
    }
}
