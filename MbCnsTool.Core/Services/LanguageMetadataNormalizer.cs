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
    public void Normalize(IReadOnlyList<SourceDocument> documents, string targetLanguage)
    {
        var metadata = ResolveMetadata(targetLanguage);
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

                root.SetAttributeValue("id", metadata.languageId);
                root.SetAttributeValue("name", metadata.languageName);
                root.SetAttributeValue("subtitle_extension", metadata.subtitleExtension);
                root.SetAttributeValue("supported_iso", metadata.supportedIso);
                root.SetAttributeValue("under_development", "false");
                continue;
            }

            foreach (var tagNode in document.Document.Descendants().Where(node => node.Name.LocalName == "tag"))
            {
                if (tagNode.Attribute("language") is not null)
                {
                    tagNode.SetAttributeValue("language", metadata.languageId);
                }
            }
        }
    }

    private static (string languageId, string languageName, string subtitleExtension, string supportedIso) ResolveMetadata(string targetLanguage)
    {
        if (targetLanguage.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
            targetLanguage.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase))
        {
            return ("繁體中文", "繁體中文", "CNT", "zh,zh-TW,zh-HK");
        }

        if (targetLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return ("简体中文", "简体中文", "CN", "zh,zh-CN");
        }

        var normalized = string.IsNullOrWhiteSpace(targetLanguage) ? "English" : targetLanguage.Trim();
        var subtitle = normalized.Length >= 2 ? normalized[..2].ToUpperInvariant() : normalized.ToUpperInvariant();
        return (normalized, normalized, subtitle, normalized);
    }
}
