namespace MbCnsTool.Core.Models;

/// <summary>
/// 一次翻译执行的摘要。
/// </summary>
public sealed class TranslationSummary
{
    /// <summary>
    /// Mod 根目录。
    /// </summary>
    public required string ModuleRootPath { get; init; }

    /// <summary>
    /// 实际输出目录。
    /// </summary>
    public required string OutputPath { get; init; }

    /// <summary>
    /// 文本总量。
    /// </summary>
    public required int TotalTextCount { get; init; }

    /// <summary>
    /// 命中缓存数量。
    /// </summary>
    public required int CacheHitCount { get; init; }

    /// <summary>
    /// 实际调用翻译数量。
    /// </summary>
    public required int ProviderCallCount { get; init; }

    /// <summary>
    /// DLL 字符串数量。
    /// </summary>
    public required int DllLiteralCount { get; init; }

    /// <summary>
    /// 运行时映射文件路径。
    /// </summary>
    public string? RuntimeMapPath { get; init; }

    /// <summary>
    /// 翻译对比文件路径。
    /// </summary>
    public string? ReviewFilePath { get; init; }

    /// <summary>
    /// 翻译对比条目数量。
    /// </summary>
    public int ReviewEntryCount { get; init; }

    /// <summary>
    /// 是否已完成最终打包。
    /// </summary>
    public bool PackageCompleted { get; init; }
}
