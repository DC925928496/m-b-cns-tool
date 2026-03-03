using System.Text.Json.Nodes;
using System.Xml.Linq;
using MbCnsTool.Core.Models;
using MbCnsTool.Core.Services;

namespace MbCnsTool.Core.Extraction;

/// <summary>
/// Mod 文本提取器（严格本地化模式）。
/// </summary>
public sealed class ModTextExtractor
{
    private readonly TextClassifier _classifier;
    private readonly DllStringScanner _dllStringScanner;

    /// <summary>
    /// 初始化提取器。
    /// </summary>
    public ModTextExtractor(TextClassifier classifier, DllStringScanner dllStringScanner)
    {
        _classifier = classifier;
        _dllStringScanner = dllStringScanner;
    }

    /// <summary>
    /// 执行严格扫描：只翻译 <c>std_module_strings_xml.xml</c>，其它文件仅用于收集 <c>{=id}</c> 引用补全语言文件。
    /// </summary>
    public ScanBundle Extract(string modPath)
    {
        var moduleRoot = ResolveModuleRoot(modPath);
        var documents = new List<SourceDocument>(capacity: 1);

        var languageDocument = LoadOrCreateCanonicalLanguageDocument(moduleRoot);
        documents.Add(languageDocument);

        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        CollectTranslationIdReferencesFromXmlAndJson(moduleRoot, references);

        foreach (var (id, defaultText) in _dllStringScanner.ScanTranslationIdReferences(moduleRoot))
        {
            TryAddReference(references, id, defaultText);
        }

        MergeReferencesIntoLanguageDocument(languageDocument.Document, references);
        var textUnits = BuildLanguageTextUnits(languageDocument.Document, languageDocument.RelativePath);
        return new ScanBundle
        {
            ModuleRootPath = moduleRoot,
            Documents = documents,
            TextUnits = textUnits,
            DllLiterals = []
        };
    }

    private static string ResolveModuleRoot(string inputPath)
    {
        if (!Directory.Exists(inputPath))
        {
            throw new DirectoryNotFoundException($"未找到 Mod 路径：{inputPath}");
        }

        if (File.Exists(Path.Combine(inputPath, "SubModule.xml")))
        {
            return inputPath;
        }

        var children = Directory
            .EnumerateDirectories(inputPath)
            .Where(directory => File.Exists(Path.Combine(directory, "SubModule.xml")))
            .ToArray();
        if (children.Length == 1)
        {
            return children[0];
        }

        throw new InvalidOperationException($"无法定位唯一 Mod 根目录：{inputPath}");
    }

    private static XmlSourceDocument LoadOrCreateCanonicalLanguageDocument(string moduleRoot)
    {
        var relativePath = Path.Combine("ModuleData", "Languages", "std_module_strings_xml.xml");
        var path = Path.Combine(moduleRoot, relativePath);
        XDocument document;
        if (File.Exists(path))
        {
            document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
        }
        else
        {
            document = CreateEmptyLanguageDocument();
        }

        EnsureLanguageDocumentShape(document);
        return new XmlSourceDocument
        {
            RelativePath = relativePath,
            Document = document
        };
    }

    private static XDocument CreateEmptyLanguageDocument()
    {
        return new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(
                "base",
                new XAttribute("type", "string"),
                new XElement("tags", new XElement("tag", new XAttribute("language", "English"))),
                new XElement("strings")));
    }

    private static void EnsureLanguageDocumentShape(XDocument document)
    {
        document.Declaration ??= new XDeclaration("1.0", "utf-8", "yes");
        var root = document.Root;
        if (root is null || !root.Name.LocalName.Equals("base", StringComparison.OrdinalIgnoreCase))
        {
            root = new XElement("base");
            document.RemoveNodes();
            document.Add(root);
        }

        root.SetAttributeValue("type", "string");

        var tags = root.Elements().FirstOrDefault(element => element.Name.LocalName == "tags");
        if (tags is null)
        {
            tags = new XElement("tags", new XElement("tag", new XAttribute("language", "English")));
            root.AddFirst(tags);
        }

        if (!tags.Elements().Any(element => element.Name.LocalName == "tag"))
        {
            tags.Add(new XElement("tag", new XAttribute("language", "English")));
        }

        if (root.Elements().All(element => element.Name.LocalName != "strings"))
        {
            root.Add(new XElement("strings"));
        }
    }

    private static void CollectTranslationIdReferencesFromXmlAndJson(string moduleRoot, IDictionary<string, string> references)
    {
        var files = Directory
            .EnumerateFiles(moduleRoot, "*.*", SearchOption.AllDirectories)
            .Where(file =>
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension is not ".xml" and not ".json")
                {
                    return false;
                }

                var normalized = file.Replace('\\', '/').ToLowerInvariant();
                if (normalized.Contains("/bin/") || normalized.Contains("/debugging/"))
                {
                    return false;
                }

                return !normalized.Contains("/moduledata/languages/");
            })
            .ToArray();

        foreach (var file in files)
        {
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension == ".xml")
            {
                TryCollectFromXml(file, references);
                continue;
            }

            if (extension == ".json")
            {
                TryCollectFromJson(file, references);
            }
        }
    }

    private static void TryCollectFromXml(string filePath, IDictionary<string, string> references)
    {
        try
        {
            var document = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);
            foreach (var element in document.Descendants())
            {
                foreach (var attribute in element.Attributes())
                {
                    TryAddReference(references, attribute.Value);
                }

                if (element.HasElements)
                {
                    continue;
                }

                TryAddReference(references, element.Value);
            }
        }
        catch
        {
            // 严格模式下：引用收集是“尽力而为”，不应因单文件损坏影响整体流程。
        }
    }

    private static void TryCollectFromJson(string filePath, IDictionary<string, string> references)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(filePath));
            if (root is null)
            {
                return;
            }

            WalkJsonForReferences(root, references);
        }
        catch
        {
            // ignored
        }
    }

    private static void WalkJsonForReferences(JsonNode node, IDictionary<string, string> references)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var entry in jsonObject)
                {
                    if (entry.Value is null)
                    {
                        continue;
                    }

                    WalkJsonForReferences(entry.Value, references);
                }
                break;
            case JsonArray jsonArray:
                foreach (var child in jsonArray)
                {
                    if (child is null)
                    {
                        continue;
                    }

                    WalkJsonForReferences(child, references);
                }
                break;
            case JsonValue jsonValue:
                if (jsonValue.TryGetValue<string>(out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    TryAddReference(references, value);
                }
                break;
        }
    }

    private static void TryAddReference(IDictionary<string, string> references, string rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue) || !rawValue.Contains("{=", StringComparison.Ordinal))
        {
            return;
        }

        var normalized = TextEncodingNormalizer.NormalizeMojibake(rawValue.Trim());
        var id = TextRules.ExtractTranslationId(normalized);
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        var defaultText = TextRules.StripTranslationIdPrefix(normalized);
        defaultText = TextEncodingNormalizer.NormalizeMojibake(defaultText);
        TryAddReference(references, id, defaultText);
    }

    private static void TryAddReference(IDictionary<string, string> references, string id, string defaultText)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(defaultText))
        {
            return;
        }

        if (!TextRules.IsTranslatableString(defaultText))
        {
            return;
        }

        if (references.ContainsKey(id))
        {
            return;
        }

        references[id] = defaultText;
    }

    private static void MergeReferencesIntoLanguageDocument(XDocument languageDocument, IReadOnlyDictionary<string, string> references)
    {
        if (references.Count == 0)
        {
            return;
        }

        var root = languageDocument.Root;
        if (root is null)
        {
            return;
        }

        var stringsNode = root.Elements().FirstOrDefault(element => element.Name.LocalName == "strings");
        if (stringsNode is null)
        {
            stringsNode = new XElement("strings");
            root.Add(stringsNode);
        }

        var existing = stringsNode
            .Elements()
            .Where(element => element.Name.LocalName == "string")
            .Select(element => element.Attribute("id")?.Value)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var (id, defaultText) in references.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (existing.Contains(id))
            {
                continue;
            }

            stringsNode.Add(new XElement(
                "string",
                new XAttribute("id", id),
                new XAttribute("text", defaultText)));
            existing.Add(id);
        }
    }

    private List<TextUnit> BuildLanguageTextUnits(XDocument languageDocument, string relativePath)
    {
        var root = languageDocument.Root;
        if (root is null)
        {
            return [];
        }

        var units = new List<TextUnit>();
        foreach (var node in root.Descendants().Where(element => element.Name.LocalName == "string"))
        {
            var id = node.Attribute("id")?.Value?.Trim();
            var textAttribute = node.Attribute("text");
            var rawText = textAttribute?.Value?.Trim() ?? string.Empty;
            var text = TextEncodingNormalizer.NormalizeMojibake(rawText);
            if (string.IsNullOrWhiteSpace(id) || !TextRules.IsTranslatableString(text))
            {
                continue;
            }

            var capturedAttribute = textAttribute;
            if (capturedAttribute is null)
            {
                continue;
            }

            var currentText = capturedAttribute.Value;
            units.Add(new TextUnit
            {
                Id = $"{relativePath}|lang|string|{id}",
                RelativePath = relativePath,
                FieldPath = $"/base/strings/string[@id='{id}'].@text",
                SourceText = text,
                Category = _classifier.Classify(relativePath, "text"),
                KeyName = "text",
                TranslationId = id,
                ApplyTranslation = translated =>
                {
                    currentText = translated;
                    capturedAttribute.Value = translated;
                },
                ReadCurrentText = () => currentText
            });
        }

        return units;
    }
}
