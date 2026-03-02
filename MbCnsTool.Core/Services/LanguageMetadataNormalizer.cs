using MbCnsTool.Core.Extraction;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 语言元数据规范化，确保产物可被游戏识别为中文语言包。
/// </summary>
public sealed class LanguageMetadataNormalizer
{
    /// <summary>
    /// 规范化文档中的语言标记。
    /// </summary>
    public void Normalize(IReadOnlyList<SourceDocument> documents)
    {
        foreach (var document in documents.OfType<XmlSourceDocument>())
        {
            var relativePath = document.RelativePath.Replace('\\', '/').ToLowerInvariant();
            if (!relativePath.Contains("/languages/"))
            {
                continue;
            }

            if (relativePath.EndsWith("language_data.xml", StringComparison.OrdinalIgnoreCase))
            {
                var root = document.Document.Root;
                if (root is null)
                {
                    continue;
                }

                root.SetAttributeValue("id", "Chinese (Simplified)");
                root.SetAttributeValue("name", "简体中文");
                root.SetAttributeValue("subtitle_extension", "CN");
                root.SetAttributeValue("supported_iso", "zh,zh-CN");
                continue;
            }

            foreach (var tagNode in document.Document.Descendants().Where(node => node.Name.LocalName == "tag"))
            {
                if (tagNode.Attribute("language") is not null)
                {
                    tagNode.SetAttributeValue("language", "Chinese (Simplified)");
                }
            }
        }
    }
}
