namespace MbCnsTool.Core.Models;

/// <summary>
/// 扫描结果，包含文本单元、文档对象与 DLL 字符串。
/// </summary>
public sealed class ScanBundle
{
    /// <summary>
    /// 实际 Mod 根目录。
    /// </summary>
    public required string ModuleRootPath { get; init; }

    /// <summary>
    /// 需要翻译的文本单元。
    /// </summary>
    public required IReadOnlyList<TextUnit> TextUnits { get; init; }

    /// <summary>
    /// 可保存的文档集合。
    /// </summary>
    public required IReadOnlyList<SourceDocument> Documents { get; init; }

    /// <summary>
    /// DLL 硬编码字符串。
    /// </summary>
    public required IReadOnlyList<DllStringLiteral> DllLiterals { get; init; }
}
