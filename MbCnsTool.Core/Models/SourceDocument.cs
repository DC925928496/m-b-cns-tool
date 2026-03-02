namespace MbCnsTool.Core.Models;

/// <summary>
/// 可保存的源文档抽象。
/// </summary>
public abstract class SourceDocument
{
    /// <summary>
    /// 文档相对于 Mod 根目录的路径。
    /// </summary>
    public required string RelativePath { get; init; }

    /// <summary>
    /// 将文档保存到指定根目录下。
    /// </summary>
    public abstract Task SaveToAsync(string targetRoot, CancellationToken cancellationToken);
}
