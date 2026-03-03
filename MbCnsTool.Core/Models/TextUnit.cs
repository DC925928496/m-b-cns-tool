using MbCnsTool.Core.Services;

namespace MbCnsTool.Core.Models;

/// <summary>
/// 可翻译文本单元，包含源信息与回写动作。
/// </summary>
public sealed class TextUnit
{
    /// <summary>
    /// 单元唯一标识。
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 来源文件相对路径。
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// 字段路径，用于日志与调试。
    /// </summary>
    public required string FieldPath { get; init; }

    /// <summary>
    /// 原始文本。
    /// </summary>
    public required string SourceText { get; init; }

    /// <summary>
    /// 文本分类。
    /// </summary>
    public required TextCategory Category { get; init; }

    /// <summary>
    /// 文本所属键名。
    /// </summary>
    public required string KeyName { get; init; }

    /// <summary>
    /// 翻译接口 ID（如 {=item_name} 中的 item_name），无接口时为 null。
    /// </summary>
    public string? TranslationId { get; init; }

    /// <summary>
    /// 回写译文动作。
    /// </summary>
    public required Action<string> ApplyTranslation { get; init; }

    /// <summary>
    /// 读取当前文本（用于打包阶段判断是否发生变更）。
    /// </summary>
    public required Func<string> ReadCurrentText { get; init; }

    /// <summary>
    /// 计算缓存键。
    /// </summary>
    public string BuildCacheKey(TranslationRunOptions options)
    {
        return $"{Category}|{options.StyleProfile}|{SourceText}";
    }
}
