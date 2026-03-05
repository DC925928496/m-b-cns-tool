using MbCnsTool.Core.Extraction;
using MbCnsTool.Core.Models;
using Mono.Cecil;

namespace MbCnsTool.Core.Services;

/// <summary>
/// 游戏版本门禁探测器（严格安全优先：无法可靠探测时返回 null）。
/// </summary>
public sealed class GameVersionGateDetector
{
    /// <summary>
    /// 尝试从 Mod 声明的主 DLL（SubModule.xml 的 DLLName）中探测其引用的 TaleWorlds.Core 版本，
    /// 并据此生成运行时注入的版本门禁配置。
    /// </summary>
    public RuntimeLocalizationVersionGate? TryDetect(string moduleRootPath)
    {
        if (string.IsNullOrWhiteSpace(moduleRootPath) || !Directory.Exists(moduleRootPath))
        {
            return null;
        }

        var declaredDllNames = SubModuleManifestReader.ResolveDeclaredDllNames(moduleRootPath);
        if (declaredDllNames.Length == 0)
        {
            return null;
        }

        var binRoot = Path.Combine(moduleRootPath, "bin");
        if (!Directory.Exists(binRoot))
        {
            return null;
        }

        var versions = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dllName in declaredDllNames)
        {
            var path = TryResolveDllPath(binRoot, dllName);
            if (path is null)
            {
                continue;
            }

            var version = TryReadReferencedCoreVersion(path);
            if (!string.IsNullOrWhiteSpace(version))
            {
                versions.Add(version);
            }
        }

        if (versions.Count == 0)
        {
            return null;
        }

        return new RuntimeLocalizationVersionGate
        {
            AllowedCoreAssemblyVersions = versions.OrderBy(v => v, StringComparer.Ordinal).ToArray()
        };
    }

    private static string? TryResolveDllPath(string binRoot, string dllName)
    {
        if (string.IsNullOrWhiteSpace(dllName))
        {
            return null;
        }

        try
        {
            var matches = Directory
                .EnumerateFiles(binRoot, dllName, SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return matches.Length == 0 ? null : matches[0];
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadReferencedCoreVersion(string dllPath)
    {
        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(dllPath, new ReaderParameters
            {
                ReadSymbols = false,
                ReadingMode = ReadingMode.Deferred
            });

            var reference = assembly.MainModule.AssemblyReferences.FirstOrDefault(r =>
                string.Equals(r.Name, "TaleWorlds.Core", StringComparison.Ordinal));
            return reference?.Version.ToString();
        }
        catch
        {
            return null;
        }
    }
}
