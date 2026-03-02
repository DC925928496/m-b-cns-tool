using System.Text;
using System.Xml;
using System.Xml.Linq;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Extraction;

/// <summary>
/// XML 源文档。
/// </summary>
public sealed class XmlSourceDocument : SourceDocument
{
    /// <summary>
    /// XML 文档对象。
    /// </summary>
    public required XDocument Document { get; init; }

    /// <inheritdoc />
    public override async Task SaveToAsync(string targetRoot, CancellationToken cancellationToken)
    {
        var targetPath = Path.Combine(targetRoot, RelativePath);
        var parentDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false,
            Async = true
        };

        await using var stream = File.Create(targetPath);
        await using var writer = XmlWriter.Create(stream, settings);
        Document.Save(writer);
        await writer.FlushAsync();
    }
}
