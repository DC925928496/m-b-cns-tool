namespace MbCnsTool.Core.Models;

/// <summary>
/// 运行时注入映射文件（数组结构）。
/// </summary>
public sealed class RuntimeLocalizationMap
{
    /// <summary>
    /// 记录结构版本。
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// 目标语言（例如 zh-CN）。
    /// </summary>
    public required string TargetLanguage { get; init; }

    /// <summary>
    /// 生成时间（UTC）。
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 版本门禁配置。
    /// </summary>
    public RuntimeLocalizationVersionGate? GameVersionGate { get; init; }

    /// <summary>
    /// 条目列表。
    /// </summary>
    public required IReadOnlyList<RuntimeLocalizationEntry> Entries { get; init; }
}

