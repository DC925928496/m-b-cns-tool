using System.Reflection;

namespace MbCnsTool.RuntimeLocalization;

internal static class VersionGateEvaluator
{
    /// <summary>
    /// 严格安全优先：任何无法判定的情况都应返回 false（禁用注入）。
    /// </summary>
    public static bool IsAllowed(RuntimeLocalizationVersionGateContract? gate)
    {
        if (gate is null)
        {
            return false;
        }

        var coreAssembly = TryFindLoadedAssembly("TaleWorlds.Core");
        if (coreAssembly is null)
        {
            return false;
        }

        var coreVersion = coreAssembly.GetName().Version;
        if (coreVersion is null)
        {
            return false;
        }

        var hasRule = false;
        if (gate.AllowedCoreAssemblyVersions is { Length: > 0 })
        {
            hasRule = true;
            var current = coreVersion.ToString();
            if (!gate.AllowedCoreAssemblyVersions.Contains(current, StringComparer.Ordinal))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(gate.CoreAssemblyVersionMin))
        {
            hasRule = true;
            if (!Version.TryParse(gate.CoreAssemblyVersionMin, out var min))
            {
                return false;
            }

            if (coreVersion < min)
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(gate.CoreAssemblyVersionMax))
        {
            hasRule = true;
            if (!Version.TryParse(gate.CoreAssemblyVersionMax, out var max))
            {
                return false;
            }

            if (coreVersion > max)
            {
                return false;
            }
        }

        return hasRule;
    }

    private static Assembly? TryFindLoadedAssembly(string name)
    {
        try
        {
            return AppDomain.CurrentDomain
                .GetAssemblies()
                .FirstOrDefault(assembly =>
                {
                    var assemblyName = assembly.GetName().Name;
                    return string.Equals(assemblyName, name, StringComparison.Ordinal);
                });
        }
        catch
        {
            return null;
        }
    }
}

