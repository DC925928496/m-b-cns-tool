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
    private readonly TextObjectLiteralScanner _textObjectLiteralScanner;

    /// <summary>
    /// 初始化提取器。
    /// </summary>
    public ModTextExtractor(TextClassifier classifier, TextObjectLiteralScanner textObjectLiteralScanner)
    {
        _classifier = classifier;
        _textObjectLiteralScanner = textObjectLiteralScanner;
    }

    /// <summary>
    /// 执行严格扫描：
    /// <list type="bullet">
    /// <item>扫描并加载 <c>ModuleData/Languages/</c> 下的语言文件（排除目标语言目录），作为可翻译文本来源；</item>
    /// <item>扫描非语言 XML/JSON 与 DLL，仅收集 <c>{=id}</c> 引用用于补全缺失条目。</item>
    /// </list>
    /// </summary>
    public ScanBundle Extract(string modPath, string targetLanguage = "zh-CN", bool scanDll = true)
    {
        var moduleRoot = ModuleRootLocator.Resolve(modPath);
        var languageIndex = new LanguageStringIndexBuilder().Build(moduleRoot, targetLanguage);

        var documents = new List<SourceDocument>();
        var languageDocuments = LoadLanguageXmlDocuments(moduleRoot, languageIndex.PreferredFolderCode);
        if (languageDocuments.Count == 0)
        {
            languageDocuments.Add(LoadOrCreateCanonicalLanguageDocument(moduleRoot));
        }

        documents.AddRange(languageDocuments);
        var primaryLanguageDocument = SelectPrimaryLanguageDocument(languageDocuments);
        if (primaryLanguageDocument is null)
        {
            throw new InvalidOperationException("未找到可用的语言文件（内部错误）。");
        }

        var references = new Dictionary<string, string>(StringComparer.Ordinal);
        var idSources = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        CollectTranslationIdReferencesFromXmlAndJson(moduleRoot, references, idSources, languageIndex.AllIds);

        var dllLiterals = new List<DllStringLiteral>();
        if (scanDll)
        {
            foreach (var literal in _textObjectLiteralScanner.ScanTextObjectStringLiterals(moduleRoot))
            {
                var normalized = TextEncodingNormalizer.NormalizeMojibake(literal.SourceText.Trim());
                var id = TextRules.ExtractTranslationId(normalized);
                if (!string.IsNullOrWhiteSpace(id))
                {
                    if (languageIndex.AllIds.Contains(id))
                    {
                        continue;
                    }

                    var defaultText = TextRules.StripTranslationIdPrefix(normalized);
                    defaultText = TextEncodingNormalizer.NormalizeMojibake(defaultText);
                    TryAddReference(references, id, defaultText);
                    RegisterIdSource(idSources, id, literal.AssemblyName);
                    continue;
                }

                if (!TextRules.IsTranslatableString(normalized))
                {
                    continue;
                }

                dllLiterals.Add(literal with { SourceText = normalized });
            }
        }

        MergeReferencesIntoLanguageDocument(primaryLanguageDocument.Document, references);
        var textUnits = new List<TextUnit>();
        foreach (var document in languageDocuments)
        {
            textUnits.AddRange(BuildLanguageTextUnits(document.Document, document.RelativePath));
        }

        return new ScanBundle
        {
            ModuleRootPath = moduleRoot,
            Documents = documents,
            TextUnits = textUnits,
            DllLiterals = dllLiterals,
            TranslationIdSources = idSources.ToDictionary(
                entry => entry.Key,
                entry => (IReadOnlyList<string>)entry.Value
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                StringComparer.Ordinal)
        };
    }

    private static List<XmlSourceDocument> LoadLanguageXmlDocuments(string moduleRoot, string? preferredFolderCode)
    {
        var documents = new List<XmlSourceDocument>();
        var languageRoot = Path.Combine(moduleRoot, "ModuleData", "Languages");
        if (!Directory.Exists(languageRoot))
        {
            return documents;
        }

        var preferredSegment = string.IsNullOrWhiteSpace(preferredFolderCode)
            ? null
            : $"/languages/{preferredFolderCode.Trim().Replace('\\', '/').ToLowerInvariant()}/";

        foreach (var xmlPath in Directory.EnumerateFiles(languageRoot, "*.xml", SearchOption.AllDirectories))
        {
            var fileName = Path.GetFileName(xmlPath);
            if (fileName.Equals("language_data.xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normalized = xmlPath.Replace('\\', '/').ToLowerInvariant();
            if (normalized.Contains("/languages/_template/") || normalized.Contains("/languages/_templates/"))
            {
                continue;
            }

            if (preferredSegment is not null && normalized.Contains(preferredSegment))
            {
                continue;
            }

            try
            {
                var document = XDocument.Load(xmlPath, LoadOptions.PreserveWhitespace);
                documents.Add(new XmlSourceDocument
                {
                    RelativePath = ToRelativePath(moduleRoot, xmlPath),
                    Document = document
                });
            }
            catch
            {
                // 单文件损坏不应中断整体扫描。
            }
        }

        return documents;
    }

    private static XmlSourceDocument? SelectPrimaryLanguageDocument(IReadOnlyList<XmlSourceDocument> documents)
    {
        if (documents.Count == 0)
        {
            return null;
        }

        var preferred = documents
            .Where(doc =>
            {
                var name = Path.GetFileName(doc.RelativePath);
                return name.Contains("strings", StringComparison.OrdinalIgnoreCase) ||
                       name.Contains("std_module_strings", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(doc => doc.RelativePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return preferred ?? documents.OrderBy(doc => doc.RelativePath, StringComparer.OrdinalIgnoreCase).First();
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

    private static void CollectTranslationIdReferencesFromXmlAndJson(
        string moduleRoot,
        IDictionary<string, string> references,
        IDictionary<string, HashSet<string>> idSources,
        IReadOnlySet<string> existingIds)
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
                TryCollectFromXml(moduleRoot, file, references, idSources, existingIds);
                continue;
            }

            if (extension == ".json")
            {
                TryCollectFromJson(moduleRoot, file, references, idSources, existingIds);
            }
        }
    }

    private static void TryCollectFromXml(
        string moduleRoot,
        string filePath,
        IDictionary<string, string> references,
        IDictionary<string, HashSet<string>> idSources,
        IReadOnlySet<string> existingIds)
    {
        try
        {
            var document = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);
            var relative = ToRelativePath(moduleRoot, filePath);
            foreach (var element in document.Descendants())
            {
                foreach (var attribute in element.Attributes())
                {
                    TryAddReference(references, idSources, attribute.Value, relative, existingIds);
                }

                if (element.HasElements)
                {
                    continue;
                }

                TryAddReference(references, idSources, element.Value, relative, existingIds);
            }
        }
        catch
        {
            // 严格模式下：引用收集是“尽力而为”，不应因单文件损坏影响整体流程。
        }
    }

    private static void TryCollectFromJson(
        string moduleRoot,
        string filePath,
        IDictionary<string, string> references,
        IDictionary<string, HashSet<string>> idSources,
        IReadOnlySet<string> existingIds)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(filePath));
            if (root is null)
            {
                return;
            }

            var relative = ToRelativePath(moduleRoot, filePath);
            WalkJsonForReferences(root, references, idSources, relative, existingIds);
        }
        catch
        {
            // ignored
        }
    }

    private static void WalkJsonForReferences(
        JsonNode node,
        IDictionary<string, string> references,
        IDictionary<string, HashSet<string>> idSources,
        string relativeFile,
        IReadOnlySet<string> existingIds)
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

                    WalkJsonForReferences(entry.Value, references, idSources, relativeFile, existingIds);
                }
                break;
            case JsonArray jsonArray:
                foreach (var child in jsonArray)
                {
                    if (child is null)
                    {
                        continue;
                    }

                    WalkJsonForReferences(child, references, idSources, relativeFile, existingIds);
                }
                break;
            case JsonValue jsonValue:
                if (jsonValue.TryGetValue<string>(out var value) && !string.IsNullOrWhiteSpace(value))
                {
                    TryAddReference(references, idSources, value, relativeFile, existingIds);
                }
                break;
        }
    }

    private static void TryAddReference(
        IDictionary<string, string> references,
        IDictionary<string, HashSet<string>> idSources,
        string rawValue,
        string relativeFile,
        IReadOnlySet<string> existingIds)
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

        RegisterIdSource(idSources, id, relativeFile);

        if (existingIds.Contains(id))
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

        EnsureLanguageDocumentShape(languageDocument);
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

    private static void RegisterIdSource(IDictionary<string, HashSet<string>> idSources, string id, string sourceFile)
    {
        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(sourceFile))
        {
            return;
        }

        if (!idSources.TryGetValue(id, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            idSources[id] = set;
        }

        set.Add(sourceFile);
    }

    private static string ToRelativePath(string moduleRoot, string filePath)
    {
        try
        {
            var relative = Path.GetRelativePath(moduleRoot, filePath);
            return relative.Replace('\\', Path.DirectorySeparatorChar);
        }
        catch
        {
            return filePath;
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
