namespace BiometricBridge.Host;

/// <summary>
/// 桥接器状态：对外服务通过它回答自身在不在、跑了多久
/// </summary>
/// <remarks>
/// <para>
/// 本类型是"功能实现"这层的最小起点。它落在 <c>BiometricBridge.Host</c> 而非宿主项目，
/// 因此换一种对外服务方式（gRPC、命名管道……）时它原样不动 —— 这正是宿主可替换的前提。
/// </para>
/// <para>
/// 后续设备相关功能也落在这一层：不引任何 HTTP 依赖，由宿主项目负责把它交给控制器。
/// </para>
/// </remarks>
public sealed class BridgeStatusService
{
    #region Fileds

    /// <summary>
    /// 起始时刻（UTC）。用构造时刻近似进程存活起点：本服务由容器在启动路径上单例构造，
    /// 之后不再重建
    /// </summary>
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    #endregion

    /// <summary>
    /// 取当前状态
    /// </summary>
    /// <returns>
    /// 桥接器运行状态
    /// </returns>
    public BridgeStatus GetStatus()
    {
        return new BridgeStatus
        {
            Name = "BiometricBridge",
            Version = typeof(BridgeStatusService).Assembly.GetName().Version?.ToString() ?? "unknown",
            Uptime = DateTimeOffset.UtcNow - _startedAt,
        };
    }
}
