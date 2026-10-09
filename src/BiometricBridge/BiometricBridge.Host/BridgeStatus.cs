namespace BiometricBridge.Host;

/// <summary>
/// 桥接器运行状态。对外服务用它回答"进程是否正常在跑"
/// </summary>
public sealed record BridgeStatus
{
    /// <summary>
    /// 应用名
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 版本号
    /// </summary>
    public required string Version { get; init; }

    /// <summary>
    /// 已运行时长
    /// </summary>
    public required TimeSpan Uptime { get; init; }
}
