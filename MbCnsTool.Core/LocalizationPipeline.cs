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
    /// 执行翻译阶段（不打包）。
    /// </summary>
    public async Task<TranslationSummary> RunTranslationStageAsync(
        TranslationRunOptions options,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        progress?.Report("开始扫描语言文件并收集本地化引用（严格模式）。");
        var extractor = new ModTextExtractor(new TextClassifier(), new DllStringScanner());
        var bundle = extractor.Extract(options.ModPath);
        progress?.Report($"扫描完成：语言条目 {bundle.TextUnits.Count}。");
        var reviewService = new TranslationReviewService();
        var reviewPath = options.ReviewFilePath ?? TranslationReviewService.ResolveDefaultPath(options.OutputPath, bundle.ModuleRootPath);

        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
        var providerFactory = new TranslationProviderFactory(httpClient);
        var providers = providerFactory.CreateAll().ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        RegisterCustomProviderIfNeeded(options, providers, httpClient);

        var glossaryService = await GlossaryService.LoadAsync(options.GlossaryFilePath, cancellationToken);
        var cachePath = options.CacheDbPath ?? Path.Combine(options.OutputPath, "cache", "translation_cache.db");
        progress?.Report($"加载术语表并打开缓存：{cachePath}");
        await using var cache = await TranslationCache.OpenAsync(cachePath, cancellationToken);
        var existingSnapshot = await reviewService.TryLoadAsync(reviewPath, cancellationToken);
        if (existingSnapshot is not null)
        {
            var imported = await reviewService.ApplySnapshotToCacheAsync(existingSnapshot, cache, cancellationToken);
            progress?.Report($"已载入历史翻译对比数据：{imported} 条。");
        }

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
        normalizer.Normalize(bundle.Documents, options.TargetLanguage);

        var runtimeMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var reviewSnapshot = reviewService.BuildSnapshot(bundle, runtimeMap, options);
        await reviewService.SaveAsync(reviewPath, reviewSnapshot, cancellationToken);
        progress?.Report($"翻译完成，已生成可编辑对比文件：{reviewPath}");

        return new TranslationSummary
        {
            ModuleRootPath = bundle.ModuleRootPath,
            OutputPath = options.OutputPath,
            TotalTextCount = bundle.TextUnits.Count,
            CacheHitCount = cacheHitCount,
            ProviderCallCount = providerCallCount,
            DllLiteralCount = bundle.DllLiterals.Count,
            RuntimeMapPath = null,
            ReviewFilePath = reviewPath,
            ReviewEntryCount = reviewSnapshot.Entries.Count,
            PackageCompleted = false
        };
    }

    /// <summary>
    /// 执行确认后的最终打包阶段。
    /// </summary>
    public async Task<TranslationSummary> RunPackageStageAsync(
        TranslationRunOptions options,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        progress?.Report("开始扫描语言文件并收集本地化引用（严格模式）。");
        var extractor = new ModTextExtractor(new TextClassifier(), new DllStringScanner());
        var bundle = extractor.Extract(options.ModPath);
        progress?.Report($"扫描完成：语言条目 {bundle.TextUnits.Count}。");

        var reviewService = new TranslationReviewService();
        var reviewPath = options.ReviewFilePath ?? TranslationReviewService.ResolveDefaultPath(options.OutputPath, bundle.ModuleRootPath);
        var cachePath = options.CacheDbPath ?? Path.Combine(options.OutputPath, "cache", "translation_cache.db");
        progress?.Report($"加载缓存并准备打包：{cachePath}");
        await using var cache = await TranslationCache.OpenAsync(cachePath, cancellationToken);
        var snapshot = await reviewService.TryLoadAsync(reviewPath, cancellationToken);
        if (snapshot is not null)
        {
            var imported = await reviewService.ApplySnapshotToCacheAsync(snapshot, cache, cancellationToken);
            progress?.Report($"已应用人工确认译文：{imported} 条。");
        }
        else
        {
            progress?.Report("未找到翻译对比文件，将直接按缓存内容打包。");
        }

        var (textCacheHitCount, dllCacheHitCount, runtimeMap) = await ApplyCacheToBundleAsync(
            bundle,
            options,
            cache,
            cancellationToken,
            progress);
        var normalizer = new LanguageMetadataNormalizer();
        normalizer.Normalize(bundle.Documents, options.TargetLanguage);

        var builder = new PackageBuilder();
        progress?.Report("开始写入文件并打包。");
        var (outputPath, runtimeMapPath) = await builder.BuildAsync(bundle, runtimeMap, options, cancellationToken);
        progress?.Report("打包完成。");

        return new TranslationSummary
        {
            ModuleRootPath = bundle.ModuleRootPath,
            OutputPath = outputPath,
            TotalTextCount = bundle.TextUnits.Count,
            CacheHitCount = textCacheHitCount + dllCacheHitCount,
            ProviderCallCount = 0,
            DllLiteralCount = bundle.DllLiterals.Count,
            RuntimeMapPath = runtimeMapPath,
            ReviewFilePath = reviewPath,
            ReviewEntryCount = snapshot?.Entries.Count ?? 0,
            PackageCompleted = true
        };
    }

    private static async Task<(int textCacheHitCount, int dllCacheHitCount, IReadOnlyDictionary<string, string> runtimeMap)> ApplyCacheToBundleAsync(
        ScanBundle bundle,
        TranslationRunOptions options,
        TranslationCache cache,
        CancellationToken cancellationToken,
        IProgress<string>? progress)
    {
        var placeholderProtector = new PlaceholderProtector();
        var textCacheHitCount = 0;
        var translatedMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var textKeyMap = bundle.TextUnits
            .GroupBy(unit => unit.BuildCacheKey(options), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var textTotal = textKeyMap.Count;
        var textCurrent = 0;
        foreach (var (cacheKey, unit) in textKeyMap)
        {
            var translated = await cache.TryGetAsync(cacheKey, cancellationToken);
            if (!string.IsNullOrWhiteSpace(translated) &&
                placeholderProtector.IsPlaceholderSafe(unit.SourceText, translated) &&
                placeholderProtector.IsDoubleBraceBlockSafe(unit.SourceText, translated))
            {
                translatedMap[cacheKey] = translated;
                textCacheHitCount++;
            }
            else
            {
                translatedMap[cacheKey] = unit.SourceText;
            }

            textCurrent++;
            if (textCurrent % 200 == 0 || textCurrent == textTotal)
            {
                progress?.Report($"打包回填进度（文本）：{textCurrent}/{textTotal}");
            }
        }

        foreach (var unit in bundle.TextUnits)
        {
            var cacheKey = unit.BuildCacheKey(options);
            if (translatedMap.TryGetValue(cacheKey, out var translated))
            {
                unit.ApplyTranslation(translated);
            }
        }

        return (textCacheHitCount, dllCacheHitCount: 0, runtimeMap: new Dictionary<string, string>(StringComparer.Ordinal));
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
}
