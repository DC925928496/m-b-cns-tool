using MbCnsTool.Core;
using MbCnsTool.Core.Abstractions;
using MbCnsTool.Core.Models;
using MbCnsTool.Core.Services;

namespace MbCnsTool.Tests;

/// <summary>
/// 主流程最小端到端测试（不依赖真实网络翻译）。
/// </summary>
public sealed class LocalizationPipelineTests
{
    [Fact]
    public async Task RunTranslationStage_Should_Generate_Project_And_Fallback_When_Placeholders_Broken()
    {
        var root = Path.Combine(Path.GetTempPath(), $"pipe-{Guid.NewGuid():N}");
        var moduleRoot = Path.Combine(root, "DemoMod");
        var outputRoot = Path.Combine(root, "output");
        Directory.CreateDirectory(moduleRoot);
        Directory.CreateDirectory(Path.Combine(moduleRoot, "ModuleData", "Languages"));

        await File.WriteAllTextAsync(Path.Combine(moduleRoot, "SubModule.xml"), "<Module></Module>");
        await File.WriteAllTextAsync(
            Path.Combine(moduleRoot, "ModuleData", "Languages", "std_module_strings_xml.xml"),
            """
            <base type="string">
              <tags>
                <tag language="English" />
              </tags>
              <strings>
                <string id="safe" text="Hello SAFE {PLAYER.NAME}" />
                <string id="unsafe" text="Hello UNSAFE {PLAYER.NAME}" />
              </strings>
            </base>
            """);

        try
        {
            var providers = new Dictionary<string, ITranslationProvider>(StringComparer.OrdinalIgnoreCase)
            {
                ["fake"] = new FakeProvider()
            };
            var pipeline = new LocalizationPipeline(providers);
            var options = new TranslationRunOptions
            {
                ModPath = moduleRoot,
                OutputPath = outputRoot,
                Mode = "external",
                TargetLanguage = "zh-CN",
                ProviderChain = ["fake"],
                MaxConcurrency = 1,
                ScanDll = false,
                ReviewFilePath = Path.Combine(outputRoot, "records", "DemoMod.mbcns_project.json"),
                CacheDbPath = Path.Combine(outputRoot, "cache", "translation_cache.db")
            };

            var summary = await pipeline.RunTranslationStageAsync(options, CancellationToken.None);
            Assert.False(summary.PackageCompleted);
            Assert.NotNull(summary.ReviewFilePath);
            Assert.True(File.Exists(summary.ReviewFilePath!));

            var project = await new TranslationProjectService().TryLoadAsync(summary.ReviewFilePath!, CancellationToken.None);
            Assert.NotNull(project);

            var safe = project!.Entries.Single(entry => entry.Id == "safe");
            Assert.Equal("你好安全 {PLAYER.NAME}", safe.TargetText);

            var unsafeEntry = project.Entries.Single(entry => entry.Id == "unsafe");
            Assert.Equal("Hello UNSAFE {PLAYER.NAME}", unsafeEntry.TargetText);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private sealed class FakeProvider : ITranslationProvider
    {
        public string Name => "Fake";
        public bool IsAvailable => true;

        public Task<string?> TranslateAsync(TranslationProviderRequest request, CancellationToken cancellationToken)
        {
            if (request.Text.Contains("UNSAFE", StringComparison.Ordinal))
            {
                return Task.FromResult<string?>("你好安全");
            }

            // 保留 __PH_0__ 等保护 token，以通过占位符安全校验。
            var translated = request.Text.Replace("Hello SAFE", "你好安全", StringComparison.Ordinal);
            return Task.FromResult<string?>(translated);
        }
    }
}
