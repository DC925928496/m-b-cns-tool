using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Packaging;

/// <summary>
/// 汉化包构建器，支持外置包与覆盖模式。
/// </summary>
public sealed class PackageBuilder
{
    /// <summary>
    /// 执行打包。
    /// </summary>
    public async Task<(string outputPath, string? runtimeMapPath)> BuildAsync(
        ScanBundle bundle,
        IReadOnlyDictionary<string, string> runtimeMap,
        TranslationRunOptions options,
        CancellationToken cancellationToken)
    {
        return options.Mode.Equals("overlay", StringComparison.OrdinalIgnoreCase)
            ? await BuildOverlayAsync(bundle, runtimeMap, cancellationToken)
            : await BuildExternalAsync(bundle, runtimeMap, options.OutputPath, cancellationToken);
    }

    private static async Task<(string outputPath, string? runtimeMapPath)> BuildOverlayAsync(
        ScanBundle bundle,
        IReadOnlyDictionary<string, string> runtimeMap,
        CancellationToken cancellationToken)
    {
        foreach (var document in bundle.Documents)
        {
            await document.SaveToAsync(bundle.ModuleRootPath, cancellationToken);
        }

        var runtimeMapPath = await WriteRuntimeMapAsync(bundle.ModuleRootPath, runtimeMap, cancellationToken);
        return (bundle.ModuleRootPath, runtimeMapPath);
    }

    private static async Task<(string outputPath, string? runtimeMapPath)> BuildExternalAsync(
        ScanBundle bundle,
        IReadOnlyDictionary<string, string> runtimeMap,
        string outputRoot,
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

        foreach (var document in bundle.Documents)
        {
            await document.SaveToAsync(packageRoot, cancellationToken);
        }

        CopyIfExists(Path.Combine(bundle.ModuleRootPath, "preview.png"), Path.Combine(packageRoot, "preview.png"));
        await GenerateSubModuleForExternalPackageAsync(bundle.ModuleRootPath, packageRoot, targetModuleName, cancellationToken);

        var runtimeMapPath = await WriteRuntimeMapAsync(packageRoot, runtimeMap, cancellationToken);
        return (packageRoot, runtimeMapPath);
    }

    private static async Task GenerateSubModuleForExternalPackageAsync(
        string sourceModuleRoot,
        string packageRoot,
        string targetModuleName,
        CancellationToken cancellationToken)
    {
        var sourceSubModulePath = Path.Combine(sourceModuleRoot, "SubModule.xml");
        if (!File.Exists(sourceSubModulePath))
        {
            return;
        }

        var sourceDocument = XDocument.Load(sourceSubModulePath);
        var sourceId = sourceDocument
            .Descendants()
            .FirstOrDefault(element => element.Name.LocalName == "Id")
            ?.Attribute("value")
            ?.Value ?? new DirectoryInfo(sourceModuleRoot).Name;

        var module = new XElement("Module",
            new XElement("Name", new XAttribute("value", $"{targetModuleName}")),
            new XElement("Id", new XAttribute("value", targetModuleName)),
            new XElement("Version", new XAttribute("value", "v1.0.0")),
            new XElement("DefaultModule", new XAttribute("value", "false")),
            new XElement("SingleplayerModule", new XAttribute("value", "true")),
            new XElement("MultiplayerModule", new XAttribute("value", "false")),
            new XElement("Official", new XAttribute("value", "false")),
            new XElement("DependedModules",
                new XElement("DependedModule", new XAttribute("Id", sourceId))),
            new XElement("SubModules"));

        var targetDocument = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), module);
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
        targetDocument.Save(writer);
        await writer.FlushAsync();
        await stream.FlushAsync(cancellationToken);
    }

    private static void CopyIfExists(string sourcePath, string targetPath)
    {
        if (!File.Exists(sourcePath))
        {
            return;
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private static async Task<string?> WriteRuntimeMapAsync(string moduleRoot, IReadOnlyDictionary<string, string> runtimeMap, CancellationToken cancellationToken)
    {
        if (runtimeMap.Count == 0)
        {
            return null;
        }

        var languageFolder = Path.Combine(moduleRoot, "ModuleData", "Languages");
        Directory.CreateDirectory(languageFolder);
        var path = Path.Combine(languageFolder, "runtime_localization.json");

        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, runtimeMap, new JsonSerializerOptions
        {
            WriteIndented = true
        }, cancellationToken);

        return path;
    }
}
