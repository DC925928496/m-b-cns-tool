using MbCnsTool.Core.Abstractions;
using MbCnsTool.Core.Extraction;
using MbCnsTool.Core.Models;
using MbCnsTool.Core.Packaging;
using MbCnsTool.Core.Services;
using MbCnsTool.Core.Services.Providers;
using System.Text;

namespace MbCnsTool.Core;

/// <summary>
/// 汉化执行主流程。
/// </summary>
public sealed class LocalizationPipeline
{
    private readonly IReadOnlyDictionary<string, ITranslationProvider>? _providerOverride;

    /// <summary>
    /// 初始化主流程。
    /// </summary>
    public LocalizationPipeline(IReadOnlyDictionary<string, ITranslationProvider>? providerOverride = null)
    {
        _providerOverride = providerOverride;
    }

    /// <summary>
    /// 执行扫描阶段：仅生成/更新工程记录，不调用翻译引擎，不写入打包产物。
    /// </summary>
    public async Task<TranslationSummary> RunScanStageAsync(
        TranslationRunOptions options,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        progress?.Report("开始扫描语言文件并收集本地化引用（严格模式）。");
        var extractor = new ModTextExtractor(new TextClassifier(), new TextObjectLiteralScanner());
        var bundle = extractor.Extract(options.ModPath, options.TargetLanguage, options.ScanDll);
        ReportDllScanDiagnostics(bundle.ModuleRootPath, options, bundle.DllLiterals.Count, progress);
        progress?.Report($"扫描完成：语言条目 {bundle.TextUnits.Count}，DLL 字面量 {bundle.DllLiterals.Count}。");

        var moduleId = new DirectoryInfo(bundle.ModuleRootPath).Name;
        var projectService = new TranslationProjectService();
        var dataRoot = ToolDataDirectory.Resolve();
        var projectPath = options.ReviewFilePath ?? TranslationProjectService.ResolveDefaultPath(dataRoot, moduleId);

        var existing = await projectService.TryLoadAsync(projectPath, cancellationToken);
        var project = BuildProject(bundle, options, existing);
        await projectService.SaveAsync(projectPath, project, cancellationToken);
        progress?.Report($"扫描完成，已生成可编辑工程记录：{projectPath}");

        return new TranslationSummary
        {
            ModuleRootPath = bundle.ModuleRootPath,
            OutputPath = dataRoot,
            TotalTextCount = bundle.TextUnits.Count,
            CacheHitCount = 0,
            ProviderCallCount = 0,
            DllLiteralCount = bundle.DllLiterals.Count,
            RuntimeMapPath = null,
            ReviewFilePath = projectPath,
            ReviewEntryCount = project.Entries.Count,
            PackageCompleted = false
        };
    }

    /// <summary>
    /// 执行翻译阶段（不打包）。
    /// </summary>
    public async Task<TranslationSummary> RunTranslationStageAsync(
        TranslationRunOptions options,
        CancellationToken cancellationToken,
        IProgress<string>? progress = null)
    {
        progress?.Report("开始扫描语言文件并收集本地化引用（严格模式）。");
        var extractor = new ModTextExtractor(new TextClassifier(), new TextObjectLiteralScanner());
        var bundle = extractor.Extract(options.ModPath, options.TargetLanguage, options.ScanDll);
        ReportDllScanDiagnostics(bundle.ModuleRootPath, options, bundle.DllLiterals.Count, progress);
        progress?.Report($"扫描完成：语言条目 {bundle.TextUnits.Count}。");
        var moduleId = new DirectoryInfo(bundle.ModuleRootPath).Name;
        var projectService = new TranslationProjectService();
        var dataRoot = ToolDataDirectory.Resolve();
        var projectPath = options.ReviewFilePath ?? TranslationProjectService.ResolveDefaultPath(dataRoot, moduleId);
        var existingProject = await projectService.TryLoadAsync(projectPath, cancellationToken);

        using var httpClient = _providerOverride is null
            ? new HttpClient { Timeout = TimeSpan.FromSeconds(60) }
            : null;
        var providers = _providerOverride ?? CreateDefaultProviders(options, httpClient!);

        var glossaryService = await GlossaryService.LoadAsync(options.GlossaryFilePath, cancellationToken);
        var cachePath = options.CacheDbPath ?? Path.Combine(dataRoot, "cache", "translation_cache.db");
        progress?.Report($"加载术语表并打开缓存：{cachePath}");
        await using var cache = await TranslationCache.OpenAsync(cachePath, cancellationToken);

        var project = BuildProject(bundle, options, existingProject);
        var imported = await ApplyProjectToCacheAsync(project, options, cache, cancellationToken);
        if (imported > 0)
        {
            progress?.Report($"已载入历史工程记录译文：{imported} 条。");
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

        IReadOnlyDictionary<string, string> dllMap = new Dictionary<string, string>(StringComparer.Ordinal);
        if (bundle.DllLiterals.Count > 0)
        {
            progress?.Report("开始翻译 DLL 硬编码文本（TextObject 字面量）。");
            dllMap = await orchestrator.TranslateDllLiteralsAsync(bundle.DllLiterals, options, cancellationToken, progress);
        }
        var normalizer = new LanguageMetadataNormalizer();
        normalizer.Normalize(bundle.Documents, options.TargetLanguage);

        UpdateProjectTargets(project, bundle, dllMap);
        await projectService.SaveAsync(projectPath, project, cancellationToken);
        progress?.Report($"翻译完成，已生成可编辑工程记录：{projectPath}");

        return new TranslationSummary
        {
            ModuleRootPath = bundle.ModuleRootPath,
            OutputPath = dataRoot,
            TotalTextCount = bundle.TextUnits.Count,
            CacheHitCount = cacheHitCount,
            ProviderCallCount = providerCallCount,
            DllLiteralCount = bundle.DllLiterals.Count,
            RuntimeMapPath = null,
            ReviewFilePath = projectPath,
            ReviewEntryCount = project.Entries.Count,
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
        var extractor = new ModTextExtractor(new TextClassifier(), new TextObjectLiteralScanner());
        var bundle = extractor.Extract(options.ModPath, options.TargetLanguage, options.ScanDll);
        ReportDllScanDiagnostics(bundle.ModuleRootPath, options, bundle.DllLiterals.Count, progress);
        progress?.Report($"扫描完成：语言条目 {bundle.TextUnits.Count}。");

        var moduleId = new DirectoryInfo(bundle.ModuleRootPath).Name;
        var projectService = new TranslationProjectService();
        var dataRoot = ToolDataDirectory.Resolve();
        var projectPath = options.ReviewFilePath ?? TranslationProjectService.ResolveDefaultPath(dataRoot, moduleId);
        var cachePath = options.CacheDbPath ?? Path.Combine(dataRoot, "cache", "translation_cache.db");
        progress?.Report($"加载缓存并准备打包：{cachePath}");
        await using var cache = await TranslationCache.OpenAsync(cachePath, cancellationToken);

        var project = await projectService.TryLoadAsync(projectPath, cancellationToken);
        if (project is not null)
        {
            var imported = await ApplyProjectToCacheAsync(project, options, cache, cancellationToken);
            progress?.Report(imported > 0 ? $"已应用人工确认译文：{imported} 条。" : "工程记录无可用译文，将直接按缓存内容打包。");
        }
        else
        {
            progress?.Report("未找到工程记录文件，将直接按缓存内容打包。");
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
        var effectiveOptions = ResolveEffectivePackageOptions(options, bundle.ModuleRootPath);
        var (outputPath, runtimeMapPath) = await builder.BuildAsync(bundle, runtimeMap, effectiveOptions, cancellationToken);
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
            ReviewFilePath = projectPath,
            ReviewEntryCount = project?.Entries.Count ?? 0,
            PackageCompleted = true
        };
    }

    private static TranslationRunOptions ResolveEffectivePackageOptions(TranslationRunOptions options, string moduleRootPath)
    {
        if (!options.Mode.Equals("external", StringComparison.OrdinalIgnoreCase))
        {
            return options;
        }

        if (!string.IsNullOrWhiteSpace(options.OutputPath))
        {
            return options;
        }

        var parent = Directory.GetParent(moduleRootPath)?.FullName;
        if (string.IsNullOrWhiteSpace(parent))
        {
            return options;
        }

        return new TranslationRunOptions
        {
            ModPath = options.ModPath,
            OutputPath = parent,
            Mode = options.Mode,
            StyleProfile = options.StyleProfile,
            TargetLanguage = options.TargetLanguage,
            GlossaryFilePath = options.GlossaryFilePath,
            CacheDbPath = options.CacheDbPath,
            ReviewFilePath = options.ReviewFilePath,
            ProviderChain = options.ProviderChain,
            MaxConcurrency = options.MaxConcurrency,
            ScanDll = options.ScanDll,
            CustomOpenAiProvider = options.CustomOpenAiProvider
        };
    }

    private static async Task<(int textCacheHitCount, int dllCacheHitCount, RuntimeLocalizationMap? runtimeMap)> ApplyCacheToBundleAsync(
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

        var dllCacheHitCount = 0;
        var runtimeTextMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var dllTotal = bundle.DllLiterals.Count;
        if (dllTotal > 0)
        {
            var dllCurrent = 0;
            foreach (var literal in bundle.DllLiterals)
            {
                var cacheKey = TranslationCacheKeyBuilder.BuildDllKey(literal.SourceText, options);
                var translated = await cache.TryGetAsync(cacheKey, cancellationToken);
                if (!string.IsNullOrWhiteSpace(translated) &&
                    placeholderProtector.IsPlaceholderSafe(literal.SourceText, translated) &&
                    placeholderProtector.IsDoubleBraceBlockSafe(literal.SourceText, translated) &&
                    !string.Equals(literal.SourceText, translated, StringComparison.Ordinal))
                {
                    runtimeTextMap[literal.SourceText] = translated;
                    dllCacheHitCount++;
                }

                dllCurrent++;
                if (dllCurrent % 300 == 0 || dllCurrent == dllTotal)
                {
                    progress?.Report($"打包回填进度（DLL）：{dllCurrent}/{dllTotal}");
                }
            }
        }

        var runtimeMap = BuildRuntimeLocalizationMap(bundle, options, runtimeTextMap);
        return (textCacheHitCount, dllCacheHitCount, runtimeMap);
    }

    private static RuntimeLocalizationMap? BuildRuntimeLocalizationMap(
        ScanBundle bundle,
        TranslationRunOptions options,
        IReadOnlyDictionary<string, string> runtimeTextMap)
    {
        if (runtimeTextMap.Count == 0)
        {
            return null;
        }

        var entries = bundle.DllLiterals
            .GroupBy(literal => literal.SourceText, StringComparer.Ordinal)
            .Select(group =>
            {
                if (!runtimeTextMap.TryGetValue(group.Key, out var translated))
                {
                    return null;
                }

                var contexts = group
                    .Select(literal => new TranslationProjectEntryContext(literal.AssemblyName, literal.TypeName, literal.MethodName))
                    .Distinct()
                    .ToArray();

                return new RuntimeLocalizationEntry
                {
                    Id = AutoIdGenerator.BuildStableId("dll_textobject", group.Key),
                    SourceText = group.Key,
                    SourceTextBase64 = string.Empty,
                    TargetText = translated,
                    Contexts = contexts
                };
            })
            .Where(entry => entry is not null)
            .Cast<RuntimeLocalizationEntry>()
            .ToArray();

        if (entries.Length == 0)
        {
            return null;
        }

        var gateDetector = new GameVersionGateDetector();
        var gate = gateDetector.TryDetect(bundle.ModuleRootPath);

        return new RuntimeLocalizationMap
        {
            TargetLanguage = options.TargetLanguage,
            GameVersionGate = gate,
            Entries = entries
        };
    }

    private static IReadOnlyDictionary<string, ITranslationProvider> CreateDefaultProviders(TranslationRunOptions options, HttpClient httpClient)
    {
        var providerFactory = new TranslationProviderFactory(httpClient);
        var providers = providerFactory.CreateAll().ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.OrdinalIgnoreCase);
        RegisterCustomProviderIfNeeded(options, providers, httpClient);
        return providers;
    }

    private static void RegisterCustomProviderIfNeeded(
        TranslationRunOptions options,
        IDictionary<string, ITranslationProvider> providers,
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

    private static TranslationProject BuildProject(ScanBundle bundle, TranslationRunOptions options, TranslationProject? existing)
    {
        var moduleId = new DirectoryInfo(bundle.ModuleRootPath).Name;

        var languageEntries = bundle.TextUnits.Select(unit => new TranslationProjectEntry
        {
            EntryKind = TranslationProjectEntryKind.LanguageString,
            Category = unit.Category.ToString(),
            SourceFile = unit.RelativePath,
            Id = unit.TranslationId ?? unit.Id,
            SourceText = unit.SourceText,
            TargetText = string.Empty,
            SourceTextBase64 = BuildBase64(unit.SourceText)
        }).ToList();

        var dllEntries = bundle.DllLiterals
            .GroupBy(literal => $"{literal.AssemblyName}\n{literal.SourceText}", StringComparer.Ordinal)
            .Select(group =>
            {
                var separator = group.Key.IndexOf('\n', StringComparison.Ordinal);
                var assemblyName = separator <= 0 ? group.First().AssemblyName : group.Key[..separator];
                var sourceText = separator <= 0 ? group.First().SourceText : group.Key[(separator + 1)..];

                var contexts = group
                    .Select(literal => new TranslationProjectEntryContext(literal.AssemblyName, literal.TypeName, literal.MethodName))
                    .Distinct()
                    .ToArray();

                return new TranslationProjectEntry
                {
                    EntryKind = TranslationProjectEntryKind.DllTextObjectHardcoded,
                    Category = TextCategory.系统.ToString(),
                    SourceFile = assemblyName,
                    Id = AutoIdGenerator.BuildStableId(assemblyName, sourceText),
                    SourceText = sourceText,
                    TargetText = string.Empty,
                    SourceTextBase64 = BuildBase64(sourceText),
                    Contexts = contexts
                };
            })
            .ToList();

        var merged = languageEntries.Concat(dllEntries).ToList();
        if (existing is not null)
        {
            var existingMap = existing.Entries
                .GroupBy(BuildEntryKey, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);

            var existingFallbackMap = existing.Entries
                .GroupBy(entry => $"{entry.EntryKind}|{entry.Id}", StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
            foreach (var entry in merged)
            {
                var key = BuildEntryKey(entry);
                if (existingMap.TryGetValue(key, out var old) && !string.IsNullOrWhiteSpace(old.TargetText))
                {
                    entry.TargetText = old.TargetText;
                    continue;
                }

                var fallbackKey = $"{entry.EntryKind}|{entry.Id}";
                if (existingFallbackMap.TryGetValue(fallbackKey, out var candidates) &&
                    candidates.Length == 1 &&
                    !string.IsNullOrWhiteSpace(candidates[0].TargetText))
                {
                    entry.TargetText = candidates[0].TargetText;
                }
            }
        }

        return new TranslationProject
        {
            ModuleId = moduleId,
            ModuleRootPath = bundle.ModuleRootPath,
            TargetLanguage = options.TargetLanguage,
            ScanDll = options.ScanDll,
            Entries = merged
        };
    }

    private static void UpdateProjectTargets(
        TranslationProject project,
        ScanBundle bundle,
        IReadOnlyDictionary<string, string> dllMap)
    {
        var languageMap = bundle.TextUnits
            .Where(unit => !string.IsNullOrWhiteSpace(unit.TranslationId ?? unit.Id))
            .GroupBy(BuildLanguageUnitKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().ReadCurrentText(), StringComparer.Ordinal);

        foreach (var entry in project.Entries.Where(e => e.EntryKind == TranslationProjectEntryKind.LanguageString))
        {
            var key = BuildEntryKey(entry);
            if (languageMap.TryGetValue(key, out var translated))
            {
                entry.TargetText = translated;
            }
        }

        foreach (var entry in project.Entries.Where(e => e.EntryKind == TranslationProjectEntryKind.DllTextObjectHardcoded))
        {
            if (dllMap.TryGetValue(entry.SourceText, out var translated))
            {
                entry.TargetText = translated;
            }
        }
    }

    private static async Task<int> ApplyProjectToCacheAsync(
        TranslationProject project,
        TranslationRunOptions options,
        TranslationCache cache,
        CancellationToken cancellationToken)
    {
        var imported = 0;
        var protector = new PlaceholderProtector();

        foreach (var entry in project.Entries)
        {
            var translated = entry.TargetText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(translated))
            {
                continue;
            }

            if (!protector.IsPlaceholderSafe(entry.SourceText, translated) ||
                !protector.IsDoubleBraceBlockSafe(entry.SourceText, translated))
            {
                continue;
            }

            string cacheKey;
            if (entry.EntryKind == TranslationProjectEntryKind.DllTextObjectHardcoded)
            {
                cacheKey = TranslationCacheKeyBuilder.BuildDllKey(entry.SourceText, options);
            }
            else
            {
                cacheKey = $"{entry.Category}|{options.StyleProfile}|{entry.SourceText}";
            }

            await cache.UpsertAsync(cacheKey, "project", entry.SourceText, translated, cancellationToken);
            imported++;
        }

        return imported;
    }

    private static string BuildEntryKey(TranslationProjectEntry entry)
    {
        if (entry.EntryKind == TranslationProjectEntryKind.LanguageString)
        {
            return $"{entry.EntryKind}|{entry.SourceFile}|{entry.Id}|{entry.SourceTextBase64}";
        }

        return $"{entry.EntryKind}|{entry.SourceFile}|{entry.Id}";
    }

    private static string BuildLanguageUnitKey(TextUnit unit)
    {
        var id = unit.TranslationId ?? unit.Id;
        return $"{TranslationProjectEntryKind.LanguageString}|{unit.RelativePath}|{id}|{BuildBase64(unit.SourceText)}";
    }

    private static string BuildBase64(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        return Convert.ToBase64String(bytes);
    }

    private static void ReportDllScanDiagnostics(
        string moduleRootPath,
        TranslationRunOptions options,
        int extractedLiteralCount,
        IProgress<string>? progress)
    {
        if (!options.ScanDll)
        {
            return;
        }

        var declared = SubModuleManifestReader.ResolveDeclaredDllNames(moduleRootPath);
        if (declared.Length == 0)
        {
            progress?.Report("已启用 DLL 扫描，但 SubModule.xml 未声明 <DLLName>，将跳过 DLL 扫描。");
            return;
        }

        var binDirectory = Path.Combine(moduleRootPath, "bin");
        if (!Directory.Exists(binDirectory))
        {
            progress?.Report($"已启用 DLL 扫描，但未找到目录：{binDirectory}（常见原因：你选的是源码仓库/未编译版本）。");
            return;
        }

        var foundDllPaths = new List<string>();
        foreach (var dllName in declared)
        {
            foundDllPaths.AddRange(Directory.EnumerateFiles(binDirectory, dllName, SearchOption.AllDirectories));
        }

        if (foundDllPaths.Count == 0)
        {
            progress?.Report($"已启用 DLL 扫描，但在 {binDirectory} 下未找到声明的 DLL：{string.Join(", ", declared)}。");
            progress?.Report("提示：请确认 Mod 目录下存在 bin/Win64_Shipping_Client/*.dll（或选择游戏 Modules 下已安装的 Mod 目录）。");
            return;
        }

        progress?.Report($"DLL 扫描：发现 {foundDllPaths.Count} 个已声明 DLL 文件，开始解析 TextObject 字面量。");

        if (extractedLiteralCount == 0)
        {
            progress?.Report("DLL 扫描完成：未提取到任何 TextObject 构造字符串字面量（可能 DLL 中未使用该模式，或参数不是字符串字面量）。");
        }
    }
}
