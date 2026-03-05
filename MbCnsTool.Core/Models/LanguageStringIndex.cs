namespace MbCnsTool.Core.Models;

/// <summary>
/// 语言文件索引（用于去重与偏好路径选择）。
/// </summary>
public sealed class LanguageStringIndex
{
    /// <summary>
    /// ModuleData/Languages 目录绝对路径。
    /// </summary>
    public required string LanguageRootPath { get; init; }

    /// <summary>
    /// 目标语言优先目录代码（例如 CNs/CNt）。可能为空。
    /// </summary>
    public string? PreferredFolderCode { get; init; }

    /// <summary>
    /// 已存在的所有 string id（遍历 ModuleData/Languages 下全部 *.xml 收集）。
    /// </summary>
    public required IReadOnlySet<string> AllIds { get; init; }
}

