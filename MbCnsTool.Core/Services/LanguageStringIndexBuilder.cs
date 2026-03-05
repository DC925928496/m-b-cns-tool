using System.Xml.Linq;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 语言文件索引构建器。
/// </summary>
public sealed class LanguageStringIndexBuilder
{
    /// <summary>
    /// 构建索引：遍历 <c>ModuleData/Languages/</c> 下所有 <c>*.xml</c> 收集已存在 id，
    /// 并根据 <c>language_data.xml</c> 与目标语言确定优先语言目录（CNs/CNt）。
    /// </summary>
    public LanguageStringIndex Build(string moduleRootPath, string targetLanguage)
    {
        var languageRoot = Path.Combine(moduleRootPath, "ModuleData", "Languages");
        var preferredFolder = ResolvePreferredFolderCode(targetLanguage);
        if (!Directory.Exists(languageRoot))
        {
            return new LanguageStringIndex
            {
                LanguageRootPath = languageRoot,
                PreferredFolderCode = preferredFolder,
                AllIds = new HashSet<string>(StringComparer.Ordinal)
            };
        }

        var preferredByLanguageData = TryResolvePreferredFolderFromLanguageData(languageRoot, preferredFolder);
        var finalPreferred = preferredByLanguageData ?? preferredFolder;

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var xmlPath in Directory.EnumerateFiles(languageRoot, "*.xml", SearchOption.AllDirectories))
        {
            TryCollectStringIds(xmlPath, ids);
        }

        return new LanguageStringIndex
        {
            LanguageRootPath = languageRoot,
            PreferredFolderCode = finalPreferred,
            AllIds = ids
        };
    }

    private static void TryCollectStringIds(string xmlPath, ISet<string> ids)
    {
        try
        {
            var document = XDocument.Load(xmlPath, LoadOptions.PreserveWhitespace);
            foreach (var node in document.Descendants().Where(element => element.Name.LocalName == "string"))
            {
                var id = node.Attribute("id")?.Value?.Trim();
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                ids.Add(id);
            }
        }
        catch
        {
            // 语言文件损坏不应中断整体扫描。
        }
    }

    private static string? TryResolvePreferredFolderFromLanguageData(string languageRoot, string? preferredFolder)
    {
        var languageDataPath = Path.Combine(languageRoot, "language_data.xml");
        if (!File.Exists(languageDataPath))
        {
            return null;
        }

        try
        {
            var document = XDocument.Load(languageDataPath, LoadOptions.PreserveWhitespace);
            var folderCodes = document
                .Descendants()
                .Where(element => element.Name.LocalName == "LanguageFile")
                .Select(element => element.Attribute("xml_path")?.Value)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!.Replace('\\', '/'))
                .Select(path =>
                {
                    var slash = path.IndexOf('/', StringComparison.Ordinal);
                    return slash <= 0 ? null : path[..slash];
                })
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (folderCodes.Length == 0)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(preferredFolder) &&
                folderCodes.Any(code => code.Equals(preferredFolder, StringComparison.OrdinalIgnoreCase)))
            {
                return preferredFolder;
            }

            // language_data.xml 存在但不包含目标语言目录时：不强行猜测，保持 null（后续逻辑可回退到 targetLanguage 推导）。
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolvePreferredFolderCode(string targetLanguage)
    {
        if (string.IsNullOrWhiteSpace(targetLanguage))
        {
            return null;
        }

        if (targetLanguage.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
            targetLanguage.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase))
        {
            return "CNt";
        }

        if (targetLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return "CNs";
        }

        return null;
    }
}

