using MbCnsTool.Core.Models;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MbCnsTool.Core.Extraction;

/// <summary>
/// DLL 字符串扫描器，提取硬编码文本。
/// </summary>
public sealed class DllStringScanner
{
    /// <summary>
    /// 扫描目录下所有 DLL。
    /// </summary>
    public IReadOnlyList<DllStringLiteral> Scan(string moduleRootPath)
    {
        var result = new List<DllStringLiteral>();
        var binDirectory = Path.Combine(moduleRootPath, "bin");
        if (!Directory.Exists(binDirectory))
        {
            return result;
        }

        var dllFiles = Directory
            .EnumerateFiles(binDirectory, "*.dll", SearchOption.AllDirectories)
            .ToArray();
        foreach (var dll in dllFiles)
        {
            try
            {
                using var assembly = AssemblyDefinition.ReadAssembly(dll);
                foreach (var type in assembly.MainModule.Types)
                {
                    foreach (var method in type.Methods.Where(method => method.HasBody))
                    {
                        foreach (var instruction in method.Body.Instructions.Where(i => i.OpCode.Code == Code.Ldstr))
                        {
                            var value = instruction.Operand?.ToString();
                            if (string.IsNullOrWhiteSpace(value) || !Services.TextRules.IsTranslatableString(value))
                            {
                                continue;
                            }

                            result.Add(new DllStringLiteral(
                                Path.GetFileName(dll),
                                type.FullName,
                                method.FullName,
                                value.Trim()));
                        }
                    }
                }
            }
            catch
            {
                continue;
            }
        }

        return result
            .GroupBy(item => item.SourceText, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToList();
    }
}
