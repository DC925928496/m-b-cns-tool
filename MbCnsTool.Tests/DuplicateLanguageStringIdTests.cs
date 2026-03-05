using MbCnsTool.Core;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Tests;

/// <summary>
/// 重复 string id 场景回归测试：不应因 ToDictionary 重复键崩溃。
/// </summary>
public sealed class DuplicateLanguageStringIdTests
{
    [Fact]
    public async Task RunScanStageAsync_Twice_Should_NotThrow_When_LanguageXml_Has_Duplicate_StringIds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dup-{Guid.NewGuid():N}");
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
                <string id="ct_unit_squad" text="squad of five" />
                <string id="ct_unit_squad" text="squad of ten" />
              </strings>
            </base>
            """);

        try
        {
            var pipeline = new LocalizationPipeline();
            var projectPath = Path.Combine(outputRoot, "records", "DemoMod.mbcns_project.json");
            var options = new TranslationRunOptions
            {
                ModPath = root,
                OutputPath = outputRoot,
                Mode = "external",
                TargetLanguage = "zh-CN",
                ScanDll = false,
                ProviderChain = ["fallback"],
                MaxConcurrency = 1,
                ReviewFilePath = projectPath
            };

            var first = await pipeline.RunScanStageAsync(options, CancellationToken.None);
            Assert.Equal(projectPath, first.ReviewFilePath);

            var second = await pipeline.RunScanStageAsync(options, CancellationToken.None);
            Assert.Equal(projectPath, second.ReviewFilePath);
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

