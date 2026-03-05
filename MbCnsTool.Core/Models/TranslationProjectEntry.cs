namespace MbCnsTool.Core.Models;

/// <summary>
/// 翻译工程条目（用于界面展示：所属文件 / id / text / 译文）。
/// </summary>
public sealed class TranslationProjectEntry
{
    /// <summary>
    /// 条目类型。
    /// </summary>
    public required TranslationProjectEntryKind EntryKind { get; init; }

    /// <summary>
    /// 文本分类（用于缓存键与翻译策略）。
    /// </summary>
    public required string Category { get; init; }

    /// <summary>
    /// 所属文件（语言 XML 为相对路径；DLL 条目为程序集名或 DLL 文件名）。
    /// </summary>
    public required string SourceFile { get; init; }

    /// <summary>
    /// 文本 id（来自 <c>{=id}</c> 或自动生成的 <c>auto_*</c>）。
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 源文。
    /// </summary>
    public required string SourceText { get; init; }

    /// <summary>
    /// 源文 Base64（UTF-8 字节），用于运行时注入的稳定匹配与抗转义差异。
    /// </summary>
    public string SourceTextBase64 { get; set; } = string.Empty;

    /// <summary>
    /// 译文（可编辑）。
    /// </summary>
    public string TargetText { get; set; } = string.Empty;

    /// <summary>
    /// 上下文集合（仅 DLL 条目可能存在）。
    /// </summary>
    public IReadOnlyList<TranslationProjectEntryContext>? Contexts { get; init; }
}
