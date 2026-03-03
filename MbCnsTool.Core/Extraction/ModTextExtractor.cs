using System.Text.Json.Nodes;
using System.Xml.Linq;
using MbCnsTool.Core.Models;
using MbCnsTool.Core.Services;

namespace MbCnsTool.Core.Extraction;

/// <summary>
/// Mod 文本提取器，支持 XML/JSON 与 DLL 扫描。
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
    /// 执行全量扫描。
    /// </summary>
    public ScanBundle Extract(string modPath)
    {
        var moduleRoot = ResolveModuleRoot(modPath);
        var documents = new List<SourceDocument>();
        var textUnits = new List<TextUnit>();

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
                return !normalized.Contains("/bin/") && !normalized.Contains("/debugging/");
            })
            .ToArray();

        foreach (var file in files)
        {
            var relativePath = Path.GetRelativePath(moduleRoot, file);
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (extension == ".xml")
            {
                ParseXmlFile(file, relativePath, documents, textUnits);
                continue;
            }

            if (extension == ".json")
            {
                ParseJsonFile(file, relativePath, documents, textUnits);
            }
        }

        var dllLiterals = _dllStringScanner.Scan(moduleRoot);
        return new ScanBundle
        {
            ModuleRootPath = moduleRoot,
            Documents = documents,
            TextUnits = textUnits,
            DllLiterals = dllLiterals
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

    private void ParseXmlFile(string filePath, string relativePath, ICollection<SourceDocument> documents, ICollection<TextUnit> textUnits)
    {
        var document = XDocument.Load(filePath, LoadOptions.PreserveWhitespace);
        documents.Add(new XmlSourceDocument
        {
            RelativePath = relativePath,
            Document = document
        });

        var idCounter = 0;
        foreach (var element in document.Descendants())
        {
            foreach (var attribute in element.Attributes().ToArray())
            {
                var key = attribute.Name.LocalName;
                var source = attribute.Value.Trim();
                if (!ShouldTranslateXmlAttribute(relativePath, element, key, source))
                {
                    continue;
                }

                idCounter++;
                var capturedAttribute = attribute;
                textUnits.Add(new TextUnit
                {
                    Id = $"{relativePath}|xml|attr|{idCounter}",
                    RelativePath = relativePath,
                    FieldPath = $"{BuildElementPath(element)}.@{key}",
                    SourceText = source,
                    Category = _classifier.Classify(relativePath, key),
                    KeyName = key,
                    TranslationId = TextRules.ExtractTranslationId(source),
                    ApplyTranslation = value => capturedAttribute.Value = value,
                    ReadCurrentText = () => capturedAttribute.Value
                });
            }

            if (element.HasElements)
            {
                continue;
            }

            var directText = element.Value.Trim();
            if (!TextRules.IsTranslatableString(directText))
            {
                continue;
            }

            idCounter++;
            var capturedElement = element;
            textUnits.Add(new TextUnit
            {
                Id = $"{relativePath}|xml|value|{idCounter}",
                RelativePath = relativePath,
                FieldPath = BuildElementPath(element),
                SourceText = directText,
                Category = _classifier.Classify(relativePath, element.Name.LocalName),
                KeyName = element.Name.LocalName,
                TranslationId = TextRules.ExtractTranslationId(directText),
                ApplyTranslation = value => capturedElement.Value = value,
                ReadCurrentText = () => capturedElement.Value
            });
        }
    }

    private void ParseJsonFile(string filePath, string relativePath, ICollection<SourceDocument> documents, ICollection<TextUnit> textUnits)
    {
        var root = JsonNode.Parse(File.ReadAllText(filePath))
            ?? throw new InvalidOperationException($"JSON 解析失败：{filePath}");

        documents.Add(new JsonSourceDocument
        {
            RelativePath = relativePath,
            RootNode = root
        });

        var idCounter = 0;
        WalkJsonNode(root, "$", string.Empty, relativePath, textUnits, ref idCounter);
    }

    private void WalkJsonNode(JsonNode node, string path, string keyName, string relativePath, ICollection<TextUnit> textUnits, ref int idCounter)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var entry in jsonObject.ToArray())
                {
                    if (entry.Value is null)
                    {
                        continue;
                    }

                    WalkJsonNode(entry.Value, $"{path}.{entry.Key}", entry.Key, relativePath, textUnits, ref idCounter);
                }
                break;
            case JsonArray jsonArray:
                for (var index = 0; index < jsonArray.Count; index++)
                {
                    var child = jsonArray[index];
                    if (child is null)
                    {
                        continue;
                    }

                    WalkJsonNode(child, $"{path}[{index}]", keyName, relativePath, textUnits, ref idCounter);
                }
                break;
            case JsonValue jsonValue:
                if (!jsonValue.TryGetValue<string>(out var value) || value is null)
                {
                    break;
                }

                var source = value.Trim();
                if (!TextRules.IsTranslatableString(source))
                {
                    break;
                }

                if (!TextRules.IsCandidateKey(keyName) && !LooksLikeSentence(source))
                {
                    break;
                }

                idCounter++;
                var parent = node.Parent;
                var currentText = source;
                textUnits.Add(new TextUnit
                {
                    Id = $"{relativePath}|json|{idCounter}",
                    RelativePath = relativePath,
                    FieldPath = path,
                    SourceText = source,
                    Category = _classifier.Classify(relativePath, keyName),
                    KeyName = keyName,
                    TranslationId = TextRules.ExtractTranslationId(source),
                    ApplyTranslation = translated =>
                    {
                        currentText = translated;
                        SetJsonValue(parent, node, translated);
                    },
                    ReadCurrentText = () => currentText
                });
                break;
        }
    }

    private static void SetJsonValue(JsonNode? parent, JsonNode currentNode, string translated)
    {
        switch (parent)
        {
            case JsonObject jsonObject:
            {
                var key = jsonObject.First(entry => ReferenceEquals(entry.Value, currentNode)).Key;
                jsonObject[key] = translated;
                break;
            }
            case JsonArray jsonArray:
            {
                var index = jsonArray.IndexOf(currentNode);
                if (index >= 0)
                {
                    jsonArray[index] = translated;
                }
                break;
            }
        }
    }

    private static bool LooksLikeSentence(string text)
    {
        return text.Contains(' ') || text.Contains('.') || text.Contains('!') || text.Contains('?');
    }

    private static bool ShouldTranslateXmlAttribute(string relativePath, XElement element, string key, string source)
    {
        if (!TextRules.IsTranslatableString(source))
        {
            return false;
        }

        if (TextRules.IsCandidateKey(key))
        {
            return true;
        }

        // name 字段中大量内容是内部标识符，默认不翻译；仅在可确定是可展示文本时放行。
        if (!key.Equals("name", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (TextRules.ExtractTranslationId(source) is not null)
        {
            return true;
        }

        if (source.Any(char.IsWhiteSpace))
        {
            return true;
        }

        var path = relativePath.Replace('\\', '/').ToLowerInvariant();
        return path.Contains("/languages/") && element.Name.LocalName.Equals("string", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildElementPath(XElement element)
    {
        var chain = element.AncestorsAndSelf()
            .Reverse()
            .Select(item =>
            {
                var siblingIndex = item.Parent?.Elements(item.Name).TakeWhile(sibling => sibling != item).Count() ?? 0;
                return $"{item.Name.LocalName}[{siblingIndex}]";
            });
        return "/" + string.Join('/', chain);
    }
}
