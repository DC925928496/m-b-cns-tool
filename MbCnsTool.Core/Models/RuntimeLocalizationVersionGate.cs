namespace MbCnsTool.Core.Models;

/// <summary>
/// 运行时注入版本门禁配置（严格安全优先：无法判定版本时应禁用注入）。
/// </summary>
public sealed class RuntimeLocalizationVersionGate
{
    /// <summary>
    /// 允许的核心程序集版本列表（字符串形式，例如 1.2.7.0）。
    /// </summary>
    public IReadOnlyList<string>? AllowedCoreAssemblyVersions { get; init; }

    /// <summary>
    /// 允许的核心程序集最小版本（可选）。
    /// </summary>
    public string? CoreAssemblyVersionMin { get; init; }

    /// <summary>
    /// 允许的核心程序集最大版本（可选）。
    /// </summary>
    public string? CoreAssemblyVersionMax { get; init; }
}

