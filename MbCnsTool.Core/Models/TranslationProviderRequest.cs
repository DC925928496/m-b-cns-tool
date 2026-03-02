namespace MbCnsTool.Core.Models;

/// <summary>
/// 发送给翻译引擎的请求体。
/// </summary>
public sealed class TranslationProviderRequest
{
    /// <summary>
    /// 需要翻译的文本。
    /// </summary>
    public required string Text { get; init; }

    /// <summary>
    /// 文本分类。
    /// </summary>
    public required TextCategory Category { get; init; }

    /// <summary>
    /// 风格模板。
    /// </summary>
    public required string StyleProfile { get; init; }

    /// <summary>
    /// 目标语言。
    /// </summary>
    public required string TargetLanguage { get; init; }

    /// <summary>
    /// 术语表文本，用于提示词增强。
    /// </summary>
    public required string GlossaryPrompt { get; init; }
}
