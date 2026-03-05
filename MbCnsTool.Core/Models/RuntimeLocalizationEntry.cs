namespace MbCnsTool.Core.Models;

/// <summary>
/// 运行时注入映射条目。
/// </summary>
public sealed class RuntimeLocalizationEntry
{
    /// <summary>
    /// 稳定 id（例如 auto_...）。
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// 源文。
    /// </summary>
    public required string SourceText { get; init; }

    /// <summary>
    /// 源文 Base64（UTF-8 字节），用于字节级稳定匹配与抗转义差异。
    /// </summary>
    public string SourceTextBase64 { get; set; } = string.Empty;

    /// <summary>
    /// 译文。
    /// </summary>
    public required string TargetText { get; init; }

    /// <summary>
    /// 上下文集合（可选，仅用于审计与定位）。
    /// </summary>
    public IReadOnlyList<TranslationProjectEntryContext>? Contexts { get; init; }
}

