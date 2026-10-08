using Avalonia.Media;
using BiometricBridge.Core.Models.Enums;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BiometricBridge.ViewModels;

/// <summary>
/// 预览画面的一格：一台设备投出的、某个位置的一路实时图。
/// </summary>
/// <remarks>
/// 分格的依据是帧自带的采集位置，不是设备类型 —— 虹膜投出左右眼两条流就是两格，
/// 指纹、人脸只有一条就是一格。视图因此不必知道对面是什么设备，也不必按型号分支。
/// </remarks>
public partial class PreviewPaneViewModel : ObservableObject
{
    /// <summary>
    /// 构造格子。
    /// </summary>
    /// <param name="position">
    /// 该格对应的采集位置；null 表示设备未指明。
    /// </param>
    public PreviewPaneViewModel(BiometricPosition? position)
    {
        Position = position;
    }

    /// <summary>
    /// 该格对应的采集位置；null 表示设备未指明。同一位置恒对应同一格。
    /// </summary>
    public BiometricPosition? Position { get; }

    /// <summary>
    /// 标题，用于区分同一设备投出的多路画面（如左右眼）。
    /// </summary>
    public string Caption => Position switch
    {
        BiometricPosition.LeftIris => "左眼",
        BiometricPosition.RightIris => "右眼",
        null or BiometricPosition.Unknown => "画面",
        _ => Position.Value.ToString()
    };

    /// <summary>
    /// 该格当前要显示的图。null 表示尚未收到帧，界面留空。
    /// </summary>
    [ObservableProperty] private IImage? _image;
}
