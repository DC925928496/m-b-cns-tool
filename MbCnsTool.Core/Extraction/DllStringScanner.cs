using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Xml.Linq;

namespace MbCnsTool.Core.Extraction;

/// <summary>
/// DLL 本地化引用扫描器。
/// </summary>
public sealed class DllStringScanner
{
    /// <summary>
    /// 严格按骑砍2本地化规范扫描：仅解析 <c>SubModule.xml</c> 中声明的主 DLL，
    /// 并从中提取形如 <c>{=id}默认英文</c> 的本地化引用，用于补全语言文件。
    /// </summary>
    public IReadOnlyDictionary<string, string> ScanTranslationIdReferences(string moduleRootPath)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        var declaredDllNames = SubModuleManifestReader.ResolveDeclaredDllNames(moduleRootPath);
        if (declaredDllNames.Length == 0)
        {
            return result;
        }

        var binDirectory = Path.Combine(moduleRootPath, "bin");
        if (!Directory.Exists(binDirectory))
        {
            return result;
        }

        foreach (var dllName in declaredDllNames)
        {
            var dllFiles = Directory
                .EnumerateFiles(binDirectory, dllName, SearchOption.AllDirectories)
                .ToArray();
            foreach (var dll in dllFiles)
            {
                TryCollectFromAssembly(dll, result);
            }
        }

        return result;
    }

    private static void TryCollectFromAssembly(string assemblyPath, IDictionary<string, string> result)
    {
        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            foreach (var type in assembly.MainModule.Types)
            {
                foreach (var method in type.Methods.Where(method => method.HasBody))
                {
                    foreach (var instruction in method.Body.Instructions.Where(i => i.OpCode.Code == Code.Ldstr))
                    {
                        var value = instruction.Operand?.ToString();
                        if (string.IsNullOrWhiteSpace(value) || !value.Contains("{=", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        var normalized = Services.TextEncodingNormalizer.NormalizeMojibake(value.Trim());
                        var id = Services.TextRules.ExtractTranslationId(normalized);
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            continue;
                        }

                        var defaultText = Services.TextRules.StripTranslationIdPrefix(normalized);
                        defaultText = Services.TextEncodingNormalizer.NormalizeMojibake(defaultText);
                        if (!Services.TextRules.IsTranslatableString(defaultText))
                        {
                            continue;
                        }

                        if (result.ContainsKey(id))
                        {
                            continue;
                        }

                        result[id] = defaultText;
                    }
                }
            }
        }
        catch
        {
            // 严格模式下：DLL 扫描是“尽力而为”的补全行为，任何解析失败都不应中断主流程。
        }
    }

    // ResolveDeclaredDllNames 已迁移到 SubModuleManifestReader（避免重复实现）。
}
