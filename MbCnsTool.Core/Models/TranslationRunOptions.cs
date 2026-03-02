namespace MbCnsTool.Core.Models;

/// <summary>
/// 翻译执行参数。
/// </summary>
public sealed class TranslationRunOptions
{
    /// <summary>
    /// 源 Mod 路径，允许传入父目录。
    /// </summary>
    public required string ModPath { get; init; }

    /// <summary>
    /// 产物输出目录。
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// 执行模式：external 或 overlay。
    /// </summary>
    public required string Mode { get; init; }

    /// <summary>
    /// 风格配置名称。
    /// </summary>
    public string StyleProfile { get; init; } = "请用骑马与砍杀2的中世纪风格进行翻译";

    /// <summary>
    /// 目标语言。
    /// </summary>
    public string TargetLanguage { get; init; } = "zh-CN";

    /// <summary>
    /// 术语表文件路径。
    /// </summary>
    public string? GlossaryFilePath { get; init; }

    /// <summary>
    /// 缓存数据库文件路径。
    /// </summary>
    public string? CacheDbPath { get; init; }

    /// <summary>
    /// 翻译引擎链路顺序。
    /// </summary>
    public IReadOnlyList<string> ProviderChain { get; init; } = ["google_free", "fallback"];

    /// <summary>
    /// 文本翻译并发数。
    /// </summary>
    public int MaxConcurrency { get; init; } = 6;

    /// <summary>
    /// 自定义 OpenAI 兼容引擎配置。
    /// </summary>
    public CustomOpenAiProviderOptions? CustomOpenAiProvider { get; init; }
}
