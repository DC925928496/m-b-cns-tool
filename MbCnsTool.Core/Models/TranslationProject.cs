namespace MbCnsTool.Core.Models;

/// <summary>
/// 翻译工程记录文件（扫描/翻译/校对/打包的唯一中间态）。
/// </summary>
public sealed class TranslationProject
{
    /// <summary>
    /// 记录结构版本。
    /// </summary>
    public int SchemaVersion { get; init; } = 1;

    /// <summary>
    /// 模块 Id（来自 SubModule.xml 的 &lt;Id value="..." /&gt;）。
    /// </summary>
    public required string ModuleId { get; init; }

    /// <summary>
    /// 模块根目录绝对路径（仅本地使用）。
    /// </summary>
    public required string ModuleRootPath { get; init; }

    /// <summary>
    /// 目标语言（例如 zh-CN）。
    /// </summary>
    public required string TargetLanguage { get; init; }

    /// <summary>
    /// 本次扫描是否包含 DLL。
    /// </summary>
    public required bool ScanDll { get; init; }

    /// <summary>
    /// 记录生成时间（UTC）。
    /// </summary>
    public DateTimeOffset GeneratedAtUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// 翻译条目列表。
    /// </summary>
    public required IReadOnlyList<TranslationProjectEntry> Entries { get; init; }
}

