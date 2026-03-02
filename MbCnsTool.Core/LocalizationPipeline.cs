using MbCnsTool.Core.Extraction;
using MbCnsTool.Core.Models;
using MbCnsTool.Core.Packaging;
using MbCnsTool.Core.Services;
using MbCnsTool.Core.Services.Providers;

namespace MbCnsTool.Core;

/// <summary>
/// 汉化执行主流程。
/// </summary>
public sealed class LocalizationPipeline
{
    /// <summary>
    /// 执行翻译、回写与打包。
    /// </summary>
    public async Task<TranslationSummary> RunAsync(
        TranslationRunOptions options,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        progress?.Report("开始扫描 Mod 文本和 DLL 字符串。");
        var extractor = new ModTextExtractor(new TextClassifier(), new DllStringScanner());
        var bundle = extractor.Extract(options.ModPath);
        progress?.Report($"扫描完成：文本条目 {bundle.TextUnits.Count}，DLL 字符串 {bundle.DllLiterals.Count}。");

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        var providerFactory = new TranslationProviderFactory(httpClient);
        var providers = providerFactory.CreateAll().ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        RegisterCustomProviderIfNeeded(options, providers, httpClient);

        if (!string.IsNullOrWhiteSpace(options.GlossaryFilePath))
        {
            var autoAdded = await GlossaryAutoTermService.AppendFrequentTermsAsync(
                options.GlossaryFilePath,
                bundle.TextUnits,
                cancellationToken,
                termTranslator: (term, token) => TranslateGlossaryTermAsync(term, options, providers, token));
            progress?.Report($"高频词自动入术语表：新增 {autoAdded} 条。");
        }

        var glossaryService = await GlossaryService.LoadAsync(options.GlossaryFilePath, cancellationToken);
        var cachePath = options.CacheDbPath ?? Path.Combine(options.OutputPath, "cache", "translation_cache.db");
        progress?.Report($"加载术语表并打开缓存：{cachePath}");
        await using var cache = await TranslationCache.OpenAsync(cachePath, cancellationToken);

        var orchestrator = new TranslationOrchestrator(
            new PlaceholderProtector(),
            glossaryService,
            cache,
            providers);

        progress?.Report("开始翻译文本内容。");
        var (cacheHitCount, providerCallCount) = await orchestrator.TranslateAndApplyAsync(
            bundle.TextUnits,
            options,
            cancellationToken,
            progress);
        var normalizer = new LanguageMetadataNormalizer();
        normalizer.Normalize(bundle.Documents);

        progress?.Report("开始翻译 DLL 硬编码字符串。");
        var runtimeMap = await orchestrator.TranslateDllLiteralsAsync(
            bundle.DllLiterals,
            options,
            cancellationToken,
            progress);
        var builder = new PackageBuilder();
        progress?.Report("开始写入文件并打包。");
        var (outputPath, runtimeMapPath) = await builder.BuildAsync(bundle, runtimeMap, options, cancellationToken);
        progress?.Report("打包完成。");

        return new TranslationSummary
        {
            ModuleRootPath = bundle.ModuleRootPath,
            OutputPath = outputPath,
            TotalTextCount = bundle.TextUnits.Count,
            CacheHitCount = cacheHitCount,
            ProviderCallCount = providerCallCount,
            DllLiteralCount = bundle.DllLiterals.Count,
            RuntimeMapPath = runtimeMapPath
        };
    }

    private static void RegisterCustomProviderIfNeeded(
        TranslationRunOptions options,
        IDictionary<string, MbCnsTool.Core.Abstractions.ITranslationProvider> providers,
        HttpClient httpClient)
    {
        var custom = options.CustomOpenAiProvider;
        if (custom is null ||
            string.IsNullOrWhiteSpace(custom.ApiKey) ||
            string.IsNullOrWhiteSpace(custom.BaseUrl) ||
            string.IsNullOrWhiteSpace(custom.Model))
        {
            return;
        }

        providers[custom.ProviderKey] = new OpenAiCompatibleTranslator(
            custom.DisplayName,
            httpClient,
            custom.ApiKey,
            custom.BaseUrl,
            custom.Model);
    }

    private static async Task<string?> TranslateGlossaryTermAsync(
        string source,
        TranslationRunOptions options,
        IReadOnlyDictionary<string, MbCnsTool.Core.Abstractions.ITranslationProvider> providers,
        CancellationToken cancellationToken)
    {
        var request = new TranslationProviderRequest
        {
            Text = source,
            Category = TextCategory.系统,
            StyleProfile = "请将术语翻译为简体中文词条，仅输出译文。",
            TargetLanguage = options.TargetLanguage,
            GlossaryPrompt = string.Empty
        };

        foreach (var providerName in options.ProviderChain.Where(name => !name.Equals("fallback", StringComparison.OrdinalIgnoreCase)))
        {
            if (!providers.TryGetValue(providerName, out var provider) || !provider.IsAvailable)
            {
                continue;
            }

            var translated = (await provider.TranslateAsync(request, cancellationToken))?.Trim();
            if (!string.IsNullOrWhiteSpace(translated) &&
                !string.Equals(translated, source, StringComparison.OrdinalIgnoreCase))
            {
                return translated;
            }
        }

        return null;
    }
}
