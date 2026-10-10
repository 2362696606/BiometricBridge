namespace BiometricBridge.Host.Config;

/// <summary>
/// 把配置里的材料目录（通常是相对路径）解析成绝对路径
/// </summary>
/// <remarks>
/// <b>不能只按工作目录解析</b>：`dotnet run` 与 IDE 启动的工作目录并不相同（前者通常是仓库根，
/// 后者是 bin 目录），只认一个会让其中一种启动方式下找不到材料，而失败又发生在首次签名时，
/// 表现为一个与"设备"无关的 500。故两个根都试，取第一个存在的。
/// </remarks>
public static class SbiMaterialDirectory
{
    /// <summary>
    /// 按真实的工作目录与应用输出目录解析
    /// </summary>
    /// <param name="configuredPath">
    /// 配置中的目录（绝对路径则原样采用）
    /// </param>
    /// <returns>
    /// 材料所在目录的绝对路径
    /// </returns>
    /// <exception cref="DirectoryNotFoundException">
    /// 候选目录均不存在，消息含已尝试的路径
    /// </exception>
    public static string Resolve(string configuredPath)
        => Resolve(configuredPath, Directory.GetCurrentDirectory(), AppContext.BaseDirectory);

    /// <summary>
    /// 按指定的两个根目录解析（供测试注入）
    /// </summary>
    /// <param name="configuredPath">
    /// 配置中的目录（绝对路径则原样采用）
    /// </param>
    /// <param name="workingDirectory">
    /// 工作目录
    /// </param>
    /// <param name="applicationBaseDirectory">
    /// 应用输出目录
    /// </param>
    /// <returns>
    /// 材料所在目录的绝对路径
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="configuredPath"/> 为空
    /// </exception>
    /// <exception cref="DirectoryNotFoundException">
    /// 候选目录均不存在，消息含已尝试的路径
    /// </exception>
    public static string Resolve(string configuredPath, string workingDirectory, string applicationBaseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredPath);

        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        string[] candidates =
        [
            Path.Combine(workingDirectory, configuredPath),
            Path.Combine(applicationBaseDirectory, configuredPath),
        ];

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException(
            $"找不到材料目录「{configuredPath}」，已试过：{string.Join("；", candidates)}");
    }
}
