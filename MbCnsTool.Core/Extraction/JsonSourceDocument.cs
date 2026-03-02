using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Extraction;

/// <summary>
/// JSON 源文档。
/// </summary>
public sealed class JsonSourceDocument : SourceDocument
{
    /// <summary>
    /// JSON 根节点。
    /// </summary>
    public required JsonNode RootNode { get; init; }

    /// <inheritdoc />
    public override async Task SaveToAsync(string targetRoot, CancellationToken cancellationToken)
    {
        var targetPath = Path.Combine(targetRoot, RelativePath);
        var parentDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        await using var stream = File.Create(targetPath);
        await JsonSerializer.SerializeAsync(stream, RootNode, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }, cancellationToken);
    }
}
