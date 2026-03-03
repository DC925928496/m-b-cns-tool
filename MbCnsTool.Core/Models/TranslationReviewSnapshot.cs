namespace MbCnsTool.Core.Models;

/// <summary>
/// 翻译对比快照文件。
/// </summary>
public sealed class TranslationReviewSnapshot
{
    /// <summary>
    /// 模块名称。
    /// </summary>
    public required string ModuleName { get; init; }

    /// <summary>
    /// 风格配置。
    /// </summary>
    public required string StyleProfile { get; init; }

    /// <summary>
    /// 目标语言。
    /// </summary>
    public required string TargetLanguage { get; init; }

    /// <summary>
    /// 生成时间（UTC）。
    /// </summary>
    public required DateTimeOffset GeneratedAtUtc { get; init; }

    /// <summary>
    /// 对比条目集合。
    /// </summary>
    public required IReadOnlyList<TranslationReviewEntry> Entries { get; init; }
}
