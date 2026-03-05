using MbCnsTool.Core;
using MbCnsTool.Core.Models;
using MbCnsTool.Core.Services;

namespace MbCnsTool.Tests;

/// <summary>
/// 扫描阶段工程记录生成测试。
/// </summary>
public sealed class ScanStageSourceFileTests
{
    [Fact]
    public async Task RunScanStageAsync_When_MissingId_ComesFrom_JsonReference_Should_Record_SourceFile_As_LanguageXml()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mod-{Guid.NewGuid():N}");
        var moduleRoot = Path.Combine(root, "DemoMod");
        var outputRoot = Path.Combine(root, "out");
        Directory.CreateDirectory(moduleRoot);
        Directory.CreateDirectory(outputRoot);

        await File.WriteAllTextAsync(Path.Combine(moduleRoot, "SubModule.xml"), "<Module></Module>");

        var languageRoot = Path.Combine(moduleRoot, "ModuleData", "Languages");
        Directory.CreateDirectory(languageRoot);
        await File.WriteAllTextAsync(
            Path.Combine(languageRoot, "demo_strings.xml"),
            """
            <base type="string">
              <strings>
                <string id="exists" text="Promotion available!" />
              </strings>
            </base>
            """);

        var configRoot = Path.Combine(moduleRoot, "ModuleData", "Demo", "Config");
        Directory.CreateDirectory(configRoot);
        await File.WriteAllTextAsync(
            Path.Combine(configRoot, "progression_config.json"),
            """
            {
              "rank": "{=demo_rank_1}Skirmisher"
            }
            """);

        try
        {
            var pipeline = new LocalizationPipeline();
            var options = new TranslationRunOptions
            {
                ModPath = root,
                OutputPath = outputRoot,
                Mode = "external",
                TargetLanguage = "zh-CN",
                ScanDll = false,
                ReviewFilePath = Path.Combine(outputRoot, "records", "DemoMod.mbcns_project.json"),
                ProviderChain = ["fallback"],
                MaxConcurrency = 1
            };

            var summary = await pipeline.RunScanStageAsync(options, CancellationToken.None);
            Assert.NotNull(summary.ReviewFilePath);

            var project = await new TranslationProjectService().TryLoadAsync(summary.ReviewFilePath!, CancellationToken.None);
            Assert.NotNull(project);

            var entry = project!.Entries.Single(e => e.EntryKind == TranslationProjectEntryKind.LanguageString && e.Id == "demo_rank_1");
            Assert.EndsWith(Path.Combine("ModuleData", "Languages", "demo_strings.xml"), entry.SourceFile, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
