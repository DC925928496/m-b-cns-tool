using System.Reflection;
using System.Runtime.Serialization.Json;
using System.Text;
using HarmonyLib;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;

namespace MbCnsTool.RuntimeLocalization;

/// <summary>
/// 运行时注入汉化子模块：对 TextObject 构造首参进行“精确匹配替换”。
/// 严格安全优先：无法读取映射文件/无法判定版本/不在允许范围时，完全禁用注入。
/// </summary>
public sealed class RuntimeLocalizationSubModule : MBSubModuleBase
{
    private static readonly object InitLock = new();
    private static volatile bool _initialized;
    private static volatile bool _enabled;

    private static IReadOnlyDictionary<string, Entry> _map = new Dictionary<string, Entry>(StringComparer.Ordinal);

    protected override void OnSubModuleLoad()
    {
        base.OnSubModuleLoad();
        TryInitialize();
    }

    private static void TryInitialize()
    {
        if (_initialized)
        {
            return;
        }

        lock (InitLock)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            try
            {
                var mapPath = ResolveRuntimeMapPath();
                var contract = TryLoadMap(mapPath);
                if (contract is null)
                {
                    return;
                }

                if (!VersionGateEvaluator.IsAllowed(contract.GameVersionGate))
                {
                    return;
                }

                var built = BuildMap(contract);
                if (built.Count == 0)
                {
                    return;
                }

                _map = built;
                PatchAllTextObjectStringCtors();
                _enabled = true;
            }
            catch
            {
                _enabled = false;
            }
        }
    }

    private static string ResolveRuntimeMapPath()
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var directory = Path.GetDirectoryName(assemblyPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("无法解析注入 DLL 所在路径。");
        }

        var moduleRoot = Path.GetFullPath(Path.Combine(directory, "..", "..", ".."));
        return Path.Combine(moduleRoot, "ModuleData", "Languages", "runtime_localization.json");
    }

    private static RuntimeLocalizationMapContract? TryLoadMap(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(path);
            var serializer = new DataContractJsonSerializer(typeof(RuntimeLocalizationMapContract));
            return serializer.ReadObject(stream) as RuntimeLocalizationMapContract;
        }
        catch
        {
            return null;
        }
    }

    private static Dictionary<string, Entry> BuildMap(RuntimeLocalizationMapContract contract)
    {
        var result = new Dictionary<string, Entry>(StringComparer.Ordinal);
        var entries = contract.Entries ?? Array.Empty<RuntimeLocalizationEntryContract>();
        foreach (var item in entries)
        {
            var source = item.SourceText ?? string.Empty;
            var target = item.TargetText ?? string.Empty;
            if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
            {
                continue;
            }

            var base64 = item.SourceTextBase64 ?? string.Empty;
            if (string.IsNullOrWhiteSpace(base64))
            {
                base64 = BuildBase64(source);
            }

            if (string.IsNullOrWhiteSpace(base64))
            {
                continue;
            }

            if (!PlaceholderSafety.IsSafe(source, target))
            {
                continue;
            }

            result[base64] = new Entry(source, target);
        }

        return result;
    }

    private static string BuildBase64(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text ?? string.Empty);
        return Convert.ToBase64String(bytes);
    }

    private static void PatchAllTextObjectStringCtors()
    {
        var harmony = new Harmony("mbcns.runtime.localization");
        var type = typeof(TextObject);
        var constructors = type
            .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Where(ctor =>
            {
                var parameters = ctor.GetParameters();
                return parameters.Length > 0 && parameters[0].ParameterType == typeof(string);
            })
            .ToArray();

        var prefix = new HarmonyMethod(typeof(RuntimeLocalizationSubModule), nameof(TextObjectCtorPrefix));
        foreach (var ctor in constructors)
        {
            try
            {
                harmony.Patch(ctor, prefix: prefix);
            }
            catch
            {
                // 严格安全优先：单个签名失败不影响整体。
            }
        }
    }

    private static void TextObjectCtorPrefix(ref string __0)
    {
        if (!_enabled)
        {
            return;
        }

        try
        {
            var input = __0;
            if (string.IsNullOrWhiteSpace(input))
            {
                return;
            }

            var key = BuildBase64(input);
            if (!_map.TryGetValue(key, out var entry))
            {
                return;
            }

            if (!PlaceholderSafety.IsSafe(entry.SourceText, entry.TargetText))
            {
                return;
            }

            __0 = entry.TargetText;
        }
        catch
        {
            // 严格安全优先：任何异常都回退原文。
        }
    }

    private readonly struct Entry
    {
        public Entry(string sourceText, string targetText)
        {
            SourceText = sourceText;
            TargetText = targetText;
        }

        public string SourceText { get; }

        public string TargetText { get; }
    }
}
