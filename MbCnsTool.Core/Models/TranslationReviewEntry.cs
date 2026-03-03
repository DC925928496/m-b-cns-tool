namespace MbCnsTool.Core.Models;

/// <summary>
/// 翻译对比条目。
/// </summary>
public sealed class TranslationReviewEntry
{
    /// <summary>
    /// 缓存键。
    /// </summary>
    public required string CacheKey { get; init; }

    /// <summary>
    /// 文本分类。
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// 来源文件相对路径。
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// 字段路径。
    /// </summary>
    public required string FieldPath { get; init; }

    /// <summary>
    /// 源文本。
    /// </summary>
    public required string SourceText { get; init; }

    /// <summary>
    /// 译文。
    /// </summary>
    public required string TargetText { get; set; }

    /// <summary>
    /// 是否来自 DLL 字符串。
    /// </summary>
    public required bool IsDllLiteral { get; init; }
}
