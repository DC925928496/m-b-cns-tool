using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using MbCnsTool.Core.Extraction;
using MbCnsTool.Core.Models;
using MbCnsTool.Core.Services;

namespace MbCnsTool.Core.Packaging;

/// <summary>
/// 汉化包构建器，支持外置包与覆盖模式。
/// </summary>
public sealed class PackageBuilder
{
    private const string RuntimeInjectorDllName = "MbCnsTool.RuntimeLocalization.dll";
    private const string RuntimeInjectorHarmonyDllName = "0Harmony.dll";
    private const string RuntimeInjectorSubModuleName = "MbCnsTool.RuntimeLocalization";
    private const string RuntimeInjectorSubModuleClassType = "MbCnsTool.RuntimeLocalization.RuntimeLocalizationSubModule";
    private const string RuntimeInjectorArtifactsDirectoryEnv = "MBCNS_RUNTIME_INJECTOR_DIR";

    /// <summary>
    /// 执行打包。
    /// </summary>
    public async Task<(string outputPath, string? runtimeMapPath)> BuildAsync(
        ScanBundle bundle,
        RuntimeLocalizationMap? runtimeMap,
        TranslationRunOptions options,
        CancellationToken cancellationToken)
    {
        return options.Mode.Equals("overlay", StringComparison.OrdinalIgnoreCase)
            ? await BuildOverlayAsync(bundle, runtimeMap, options.TargetLanguage, cancellationToken)
            : await BuildExternalAsync(bundle, runtimeMap, options.OutputPath, options.TargetLanguage, cancellationToken);
    }

    private static async Task<(string outputPath, string? runtimeMapPath)> BuildOverlayAsync(
        ScanBundle bundle,
        RuntimeLocalizationMap? runtimeMap,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        if (runtimeMap is not null && runtimeMap.Entries.Count > 0)
        {
            throw new InvalidOperationException("覆盖模式不支持 DLL 运行时注入条目（为避免修改原 Mod 的 SubModule 与 bin 目录）。请改用 external 外置包模式。");
        }

        foreach (var document in bundle.Documents)
        {
            await document.SaveToAsync(bundle.ModuleRootPath, cancellationToken);
        }

        await EnsureLanguageLayoutAsync(bundle.ModuleRootPath, targetLanguage, cancellationToken);
        var runtimeMapPath = await WriteRuntimeMapAsync(bundle.ModuleRootPath, runtimeMap, cancellationToken);
        return (bundle.ModuleRootPath, runtimeMapPath);
    }

    private static async Task<(string outputPath, string? runtimeMapPath)> BuildExternalAsync(
        ScanBundle bundle,
        RuntimeLocalizationMap? runtimeMap,
        string outputRoot,
        string targetLanguage,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputRoot);
        var sourceModuleName = new DirectoryInfo(bundle.ModuleRootPath).Name;
        var targetModuleName = $"{sourceModuleName}_CNs";
        var packageRoot = Path.Combine(outputRoot, targetModuleName);
        if (Directory.Exists(packageRoot))
        {
            Directory.Delete(packageRoot, recursive: true);
        }

        Directory.CreateDirectory(packageRoot);

        var changedUnits = bundle.TextUnits
            .Where(unit => IsUnitChanged(unit))
            .ToArray();
        var changedPaths = changedUnits
            .Select(unit => NormalizePath(unit.RelativePath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var document in bundle.Documents)
        {
            var relativePath = NormalizePath(document.RelativePath);
            if (document is XmlSourceDocument)
            {
                if (relativePath.Contains("/languages/"))
                {
                    await document.SaveToAsync(packageRoot, cancellationToken);
                }

                continue;
            }

            if (changedPaths.Contains(relativePath))
            {
                await document.SaveToAsync(packageRoot, cancellationToken);
            }
        }

        await WriteXsltPatchFilesAsync(packageRoot, changedUnits, cancellationToken);
        await WriteFallbackLanguageFileIfNeededAsync(packageRoot, bundle.TextUnits, cancellationToken);
        CopyIfExists(Path.Combine(bundle.ModuleRootPath, "preview.png"), Path.Combine(packageRoot, "preview.png"));
        var includeRuntimeInjector = runtimeMap is not null && runtimeMap.Entries.Count > 0;
        await GenerateSubModuleForExternalPackageAsync(bundle.ModuleRootPath, packageRoot, targetModuleName, includeRuntimeInjector, cancellationToken);
        if (includeRuntimeInjector)
        {
            CopyRuntimeInjectorArtifacts(packageRoot);
        }

        await EnsureLanguageLayoutAsync(packageRoot, targetLanguage, cancellationToken);
        var runtimeMapPath = await WriteRuntimeMapAsync(packageRoot, runtimeMap, cancellationToken);
        return (packageRoot, runtimeMapPath);
    }

    private static async Task GenerateSubModuleForExternalPackageAsync(
        string sourceModuleRoot,
        string packageRoot,
        string targetModuleName,
        bool includeRuntimeInjector,
        CancellationToken cancellationToken)
    {
        var sourceSubModulePath = Path.Combine(sourceModuleRoot, "SubModule.xml");
        if (!File.Exists(sourceSubModulePath))
        {
            return;
        }

        var sourceDocument = XDocument.Load(sourceSubModulePath);
        var module = sourceDocument.Root;
        if (module is null)
        {
            return;
        }

        var sourceId = module
            .Elements()
            .FirstOrDefault(element => element.Name.LocalName == "Id")
            ?.Attribute("value")
            ?.Value ?? new DirectoryInfo(sourceModuleRoot).Name;

        var xmlNamespace = module.Name.Namespace;
        SetValueElement(module, xmlNamespace, "Name", targetModuleName);
        SetValueElement(module, xmlNamespace, "Id", targetModuleName);
        SetValueElement(module, xmlNamespace, "DefaultModule", "false");
        SetValueElement(module, xmlNamespace, "SingleplayerModule", "true");
        SetValueElement(module, xmlNamespace, "MultiplayerModule", "false");
        SetValueElement(module, xmlNamespace, "Official", "false");

        ReplaceChildElement(
            module,
            xmlNamespace,
            "DependedModules",
            new XElement(
                xmlNamespace + "DependedModules",
                new XElement(xmlNamespace + "DependedModule", new XAttribute("Id", sourceId))));

        // 外置汉化包不应重复加载原 Mod 的运行时代码，仅保留数据声明（如 Xmls）。
        ReplaceChildElement(
            module,
            xmlNamespace,
            "SubModules",
            includeRuntimeInjector
                ? new XElement(xmlNamespace + "SubModules", BuildRuntimeInjectorSubModuleElement(xmlNamespace))
                : new XElement(xmlNamespace + "SubModules"));
        FilterMissingXmlEntries(module, packageRoot);
        EnsureLanguageDataXmlEntry(module, xmlNamespace);

        sourceDocument.Declaration ??= new XDeclaration("1.0", "utf-8", "yes");
        var targetPath = Path.Combine(packageRoot, "SubModule.xml");

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false,
            Async = true
        };
        await using var stream = File.Create(targetPath);
        await using var writer = XmlWriter.Create(stream, settings);
        sourceDocument.Save(writer);
        await writer.FlushAsync();
        await stream.FlushAsync(cancellationToken);
    }

    private static XElement BuildRuntimeInjectorSubModuleElement(XNamespace xmlNamespace)
    {
        return new XElement(
            xmlNamespace + "SubModule",
            new XElement(xmlNamespace + "Name", new XAttribute("value", RuntimeInjectorSubModuleName)),
            new XElement(xmlNamespace + "DLLName", new XAttribute("value", RuntimeInjectorDllName)),
            new XElement(xmlNamespace + "SubModuleClassType", new XAttribute("value", RuntimeInjectorSubModuleClassType)));
    }

    private static void FilterMissingXmlEntries(XElement module, string packageRoot)
    {
        var xmls = module.Elements().FirstOrDefault(element => element.Name.LocalName == "Xmls");
        if (xmls is null)
        {
            return;
        }

        foreach (var xmlNode in xmls.Elements().Where(element => element.Name.LocalName == "XmlNode").ToArray())
        {
            var xmlName = xmlNode
                .Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "XmlName");
            var path = xmlName?.Attribute("path")?.Value;
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var normalized = path.Replace('/', Path.DirectorySeparatorChar);
            var xmlPath = Path.Combine(packageRoot, normalized);
            var xsltPath = Path.ChangeExtension(xmlPath, ".xslt");
            if (File.Exists(xmlPath) || File.Exists(xsltPath))
            {
                continue;
            }

            xmlNode.Remove();
        }
    }

    private static void EnsureLanguageDataXmlEntry(XElement module, XNamespace xmlNamespace)
    {
        var xmls = module.Elements().FirstOrDefault(element => element.Name.LocalName == "Xmls");
        if (xmls is null)
        {
            xmls = new XElement(xmlNamespace + "Xmls");
            module.Add(xmls);
        }

        var languageDataExists = xmls
            .Descendants()
            .Where(element => element.Name.LocalName == "XmlName")
            .Any(element => string.Equals(
                element.Attribute("path")?.Value,
                "ModuleData/Languages/language_data.xml",
                StringComparison.OrdinalIgnoreCase));
        if (languageDataExists)
        {
            return;
        }

        xmls.Add(
            new XElement(
                xmlNamespace + "XmlNode",
                new XElement(
                    xmlNamespace + "XmlName",
                    new XAttribute("id", "LanguagesData"),
                    new XAttribute("path", "ModuleData/Languages/language_data.xml"))));
    }

    private static void SetValueElement(XElement module, XNamespace xmlNamespace, string localName, string value)
    {
        var element = module.Elements().FirstOrDefault(item => item.Name.LocalName == localName);
        if (element is null)
        {
            element = new XElement(xmlNamespace + localName);
            module.Add(element);
        }

        element.SetAttributeValue("value", value);
    }

    private static void ReplaceChildElement(XElement module, XNamespace xmlNamespace, string localName, XElement replacement)
    {
        var matched = module.Elements().Where(item => item.Name.LocalName == localName).ToArray();
        if (matched.Length == 0)
        {
            module.Add(replacement);
            return;
        }

        matched[0].ReplaceWith(replacement);
        for (var index = 1; index < matched.Length; index++)
        {
            matched[index].Remove();
        }
    }

    private static void CopyIfExists(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private static void CopyRuntimeInjectorArtifacts(string packageRoot)
    {
        var binFolder = Path.Combine(packageRoot, "bin", "Win64_Shipping_Client");
        Directory.CreateDirectory(binFolder);

        var artifactsDir = ResolveRuntimeInjectorArtifactsDirectory();
        var runtimeInjectorDll = Path.Combine(artifactsDir, RuntimeInjectorDllName);
        var harmonyDll = Path.Combine(artifactsDir, RuntimeInjectorHarmonyDllName);

        if (!File.Exists(runtimeInjectorDll))
        {
            throw new InvalidOperationException($"未找到运行时注入 DLL：{runtimeInjectorDll}。请确保工具发布目录包含 {RuntimeInjectorDllName}，或设置环境变量 {RuntimeInjectorArtifactsDirectoryEnv} 指向包含该文件的目录。");
        }

        if (!File.Exists(harmonyDll))
        {
            throw new InvalidOperationException($"未找到 Harmony 依赖：{harmonyDll}。请确保工具发布目录包含 {RuntimeInjectorHarmonyDllName}，或设置环境变量 {RuntimeInjectorArtifactsDirectoryEnv} 指向包含该文件的目录。");
        }

        File.Copy(runtimeInjectorDll, Path.Combine(binFolder, RuntimeInjectorDllName), overwrite: true);
        File.Copy(harmonyDll, Path.Combine(binFolder, RuntimeInjectorHarmonyDllName), overwrite: true);
    }

    private static string ResolveRuntimeInjectorArtifactsDirectory()
    {
        var configured = Environment.GetEnvironmentVariable(RuntimeInjectorArtifactsDirectoryEnv);
        if (!string.IsNullOrWhiteSpace(configured) && Directory.Exists(configured))
        {
            return configured;
        }

        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            baseDir,
            Path.Combine(baseDir, "runtime_injector"),
            Path.Combine(baseDir, "runtime-localization"),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "MbCnsTool.RuntimeLocalization", "bin", "Release", "net472")),
            Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "MbCnsTool.RuntimeLocalization", "bin", "Debug", "net472"))
        };

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(candidate))
            {
                continue;
            }

            if (File.Exists(Path.Combine(candidate, RuntimeInjectorDllName)) &&
                File.Exists(Path.Combine(candidate, RuntimeInjectorHarmonyDllName)))
            {
                return candidate;
            }
        }

        return baseDir;
    }

    private static async Task<string?> WriteRuntimeMapAsync(string moduleRoot, RuntimeLocalizationMap? runtimeMap, CancellationToken cancellationToken)
    {
        if (runtimeMap is null || runtimeMap.Entries.Count == 0)
        {
            return null;
        }

        var languageFolder = Path.Combine(moduleRoot, "ModuleData", "Languages");
        Directory.CreateDirectory(languageFolder);
        var path = Path.Combine(languageFolder, "runtime_localization.json");

        var service = new RuntimeLocalizationMapService();
        await service.SaveAsync(path, runtimeMap, cancellationToken);

        return path;
    }

    private static async Task EnsureLanguageLayoutAsync(string moduleRoot, string targetLanguage, CancellationToken cancellationToken)
    {
        var metadata = ResolveLanguageMetadata(targetLanguage);
        var languageRoot = Path.Combine(moduleRoot, "ModuleData", "Languages");
        if (!Directory.Exists(languageRoot))
        {
            return;
        }

        var targetFolder = Path.Combine(languageRoot, metadata.folderCode);
        Directory.CreateDirectory(targetFolder);

        foreach (var languageDataPath in Directory.EnumerateFiles(languageRoot, "language_data.xml", SearchOption.AllDirectories))
        {
            var parent = Path.GetDirectoryName(languageDataPath);
            if (!string.Equals(parent, targetFolder, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(languageDataPath);
            }
        }

        var candidateFiles = Directory
            .EnumerateFiles(languageRoot, "*.xml", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith("language_data.xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var copiedFiles = new List<string>();
        foreach (var sourcePath in candidateFiles)
        {
            if (sourcePath.StartsWith(targetFolder, StringComparison.OrdinalIgnoreCase))
            {
                copiedFiles.Add(sourcePath);
                continue;
            }

            var targetPath = Path.Combine(targetFolder, Path.GetFileName(sourcePath));
            File.Copy(sourcePath, targetPath, overwrite: true);
            copiedFiles.Add(targetPath);
        }

        var normalizedFiles = copiedFiles
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var filePath in normalizedFiles)
        {
            NormalizeLanguageXml(filePath, metadata.languageId);
        }

        var languageEntries = normalizedFiles
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (languageEntries.Length == 0)
        {
            return;
        }

        var languageData = new XElement(
            "LanguageData",
            new XAttribute("id", metadata.languageId),
            new XAttribute("name", metadata.languageName),
            new XAttribute("subtitle_extension", metadata.subtitleExtension),
            new XAttribute("supported_iso", metadata.supportedIso),
            new XAttribute("under_development", "false"),
            languageEntries.Select(fileName => new XElement("LanguageFile", new XAttribute("xml_path", $"{metadata.folderCode}/{fileName}"))));
        var document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), languageData);
        var generatedLanguageDataPath = Path.Combine(targetFolder, "language_data.xml");
        await SaveXmlDocumentAsync(document, generatedLanguageDataPath, cancellationToken);
        await SaveXmlDocumentAsync(document, Path.Combine(languageRoot, "language_data.xml"), cancellationToken);
    }

    private static void NormalizeLanguageXml(string xmlPath, string languageId)
    {
        var document = XDocument.Load(xmlPath, LoadOptions.PreserveWhitespace);
        var root = document.Root;
        if (root is null)
        {
            return;
        }

        var xmlNamespace = root.Name.Namespace;
        var tags = root.Elements().FirstOrDefault(element => element.Name.LocalName == "tags");
        if (tags is null)
        {
            tags = new XElement(xmlNamespace + "tags");
            var stringsNode = root.Elements().FirstOrDefault(element => element.Name.LocalName == "strings");
            if (stringsNode is null)
            {
                root.AddFirst(tags);
            }
            else
            {
                stringsNode.AddBeforeSelf(tags);
            }
        }

        var tagNodes = tags.Elements().Where(element => element.Name.LocalName == "tag").ToArray();
        if (tagNodes.Length == 0)
        {
            tags.Add(new XElement(xmlNamespace + "tag", new XAttribute("language", languageId)));
        }
        else
        {
            foreach (var tagNode in tagNodes)
            {
                tagNode.SetAttributeValue("language", languageId);
            }
        }

        document.Save(xmlPath);
    }

    private static async Task SaveXmlDocumentAsync(XDocument document, string targetPath, CancellationToken cancellationToken)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false,
            Async = true
        };
        await using var stream = File.Create(targetPath);
        await using var writer = XmlWriter.Create(stream, settings);
        document.Save(writer);
        await writer.FlushAsync();
        await stream.FlushAsync(cancellationToken);
    }

    private static bool IsUnitChanged(TextUnit unit)
    {
        var current = unit.ReadCurrentText().Trim();
        return !string.Equals(unit.SourceText, current, StringComparison.Ordinal);
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').ToLowerInvariant();
    }

    private static async Task WriteFallbackLanguageFileIfNeededAsync(
        string moduleRoot,
        IReadOnlyList<TextUnit> textUnits,
        CancellationToken cancellationToken)
    {
        var languageRoot = Path.Combine(moduleRoot, "ModuleData", "Languages");
        if (Directory.Exists(languageRoot))
        {
            var hasLanguageXml = Directory
                .EnumerateFiles(languageRoot, "*.xml", SearchOption.AllDirectories)
                .Any(path => !path.EndsWith("language_data.xml", StringComparison.OrdinalIgnoreCase));
            if (hasLanguageXml)
            {
                return;
            }
        }

        var interfaceEntries = textUnits
            .Where(unit => unit.TranslationId is not null && IsUnitChanged(unit))
            .Select(unit =>
            {
                var id = unit.TranslationId!;
                var translated = TextRules.StripTranslationIdPrefix(unit.ReadCurrentText());
                if (string.IsNullOrWhiteSpace(translated))
                {
                    translated = TextRules.StripTranslationIdPrefix(unit.SourceText);
                }

                return (id, translated);
            })
            .Where(entry => !string.IsNullOrWhiteSpace(entry.id) && !string.IsNullOrWhiteSpace(entry.translated))
            .ToArray();
        if (interfaceEntries.Length == 0)
        {
            return;
        }

        Directory.CreateDirectory(languageRoot);
        var stringsPath = Path.Combine(languageRoot, "std_module_strings_xml.xml");

        var document = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement(
                "base",
                new XAttribute("type", "string"),
                new XElement(
                    "tags",
                    new XElement("tag", new XAttribute("language", "English"))),
                new XElement(
                    "strings",
                    interfaceEntries
                        .OrderBy(entry => entry.id, StringComparer.Ordinal)
                        .Select(entry => new XElement(
                            "string",
                            new XAttribute("id", entry.id),
                            new XAttribute("text", entry.translated))))));

        await SaveXmlDocumentAsync(document, stringsPath, cancellationToken);
    }

    private static async Task WriteXsltPatchFilesAsync(
        string moduleRoot,
        IReadOnlyList<TextUnit> changedUnits,
        CancellationToken cancellationToken)
    {
        var xmlGroups = changedUnits
            .Where(unit =>
                unit.TranslationId is null &&
                Path.GetExtension(unit.RelativePath).Equals(".xml", StringComparison.OrdinalIgnoreCase) &&
                !NormalizePath(unit.RelativePath).Contains("/languages/"))
            .GroupBy(unit => NormalizePath(unit.RelativePath), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var group in xmlGroups)
        {
            var unitMap = new Dictionary<string, TextUnit>(StringComparer.OrdinalIgnoreCase);
            foreach (var unit in group)
            {
                unitMap[unit.FieldPath] = unit;
            }

            var templates = new List<XElement>();
            foreach (var unit in unitMap.Values)
            {
                if (!TryBuildXPath(unit.FieldPath, out var matchPath, out var attributeName))
                {
                    continue;
                }

                var translated = unit.ReadCurrentText().Trim();
                if (string.IsNullOrWhiteSpace(translated))
                {
                    continue;
                }

                if (attributeName is null)
                {
                    templates.Add(
                        new XElement(
                            XName.Get("template", XslNamespace.NamespaceName),
                            new XAttribute("match", $"{matchPath}/text()"),
                            new XElement(XName.Get("text", XslNamespace.NamespaceName), translated)));
                    continue;
                }

                templates.Add(
                    new XElement(
                        XName.Get("template", XslNamespace.NamespaceName),
                        new XAttribute("match", $"{matchPath}/@{attributeName}"),
                        new XElement(
                            XName.Get("attribute", XslNamespace.NamespaceName),
                            new XAttribute("name", attributeName),
                            translated)));
            }

            if (templates.Count == 0)
            {
                continue;
            }

            var patchPath = Path.ChangeExtension(Path.Combine(moduleRoot, group.First().RelativePath), ".xslt");
            var parent = Path.GetDirectoryName(patchPath);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                Directory.CreateDirectory(parent);
            }

            var document = new XDocument(
                new XDeclaration("1.0", "utf-8", "yes"),
                new XElement(
                    XName.Get("stylesheet", XslNamespace.NamespaceName),
                    new XAttribute("version", "1.0"),
                    new XAttribute(XNamespace.Xmlns + "xsl", XslNamespace.NamespaceName),
                    new XElement(XName.Get("output", XslNamespace.NamespaceName), new XAttribute("omit-xml-declaration", "yes")),
                    new XElement(
                        XName.Get("template", XslNamespace.NamespaceName),
                        new XAttribute("match", "@*|node()"),
                        new XElement(
                            XName.Get("copy", XslNamespace.NamespaceName),
                            new XElement(XName.Get("apply-templates", XslNamespace.NamespaceName), new XAttribute("select", "@*|node()")))),
                    templates));
            await SaveXmlDocumentAsync(document, patchPath, cancellationToken);
        }
    }

    private static bool TryBuildXPath(string fieldPath, out string xpath, out string? attributeName)
    {
        xpath = string.Empty;
        attributeName = null;
        if (string.IsNullOrWhiteSpace(fieldPath) || !fieldPath.StartsWith('/'))
        {
            return false;
        }

        var elementPath = fieldPath;
        var attributeMarker = fieldPath.LastIndexOf(".@", StringComparison.Ordinal);
        if (attributeMarker >= 0)
        {
            attributeName = fieldPath[(attributeMarker + 2)..];
            elementPath = fieldPath[..attributeMarker];
        }

        xpath = IndexRegex.Replace(
            elementPath,
            match => $"[{int.Parse(match.Groups[1].Value) + 1}]");
        return true;
    }

    private static (string folderCode, string languageId, string languageName, string subtitleExtension, string supportedIso) ResolveLanguageMetadata(string targetLanguage)
    {
        if (targetLanguage.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase) ||
            targetLanguage.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase))
        {
            return ("CNt", "繁體中文", "繁體中文", "CNT", "zh,zh-TW,zh-HK");
        }

        if (targetLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
        {
            return ("CNs", "简体中文", "简体中文", "CN", "zh,zh-CN");
        }

        var normalized = string.IsNullOrWhiteSpace(targetLanguage) ? "English" : targetLanguage.Trim();
        var folder = normalized.Replace("-", string.Empty, StringComparison.OrdinalIgnoreCase);
        var subtitle = normalized.Length >= 2 ? normalized[..2].ToUpperInvariant() : normalized.ToUpperInvariant();
        return (folder, normalized, normalized, subtitle, normalized);
    }

    private static readonly XNamespace XslNamespace = "http://www.w3.org/1999/XSL/Transform";
    private static readonly Regex IndexRegex = new(@"\[(\d+)\]", RegexOptions.Compiled);
}
