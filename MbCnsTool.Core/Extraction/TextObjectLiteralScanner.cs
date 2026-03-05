using MbCnsTool.Core.Models;
using MbCnsTool.Core.Services;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MbCnsTool.Core.Extraction;

/// <summary>
/// 扫描 DLL 中 <c>new TextObject("字面量", ...)</c> 的字符串字面量（严格限定范围）。
/// </summary>
public sealed class TextObjectLiteralScanner
{
    public IReadOnlyList<DllStringLiteral> ScanTextObjectStringLiterals(string moduleRootPath)
    {
        var literals = new List<DllStringLiteral>();

        var declaredDllNames = SubModuleManifestReader.ResolveDeclaredDllNames(moduleRootPath);
        if (declaredDllNames.Length == 0)
        {
            return literals;
        }

        var binDirectory = Path.Combine(moduleRootPath, "bin");
        if (!Directory.Exists(binDirectory))
        {
            return literals;
        }

        foreach (var dllName in declaredDllNames)
        {
            foreach (var dllPath in Directory.EnumerateFiles(binDirectory, dllName, SearchOption.AllDirectories))
            {
                TryCollectFromAssembly(dllPath, literals);
            }
        }

        return literals;
    }

    private static void TryCollectFromAssembly(string assemblyPath, ICollection<DllStringLiteral> result)
    {
        try
        {
            using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath);
            foreach (var type in assembly.MainModule.Types)
            {
                foreach (var method in type.Methods.Where(m => m.HasBody))
                {
                    TryCollectFromMethod(Path.GetFileName(assemblyPath), type, method, result);
                }
            }
        }
        catch
        {
            // DLL 扫描为“尽力而为”，不得影响主流程。
        }
    }

    private static void TryCollectFromMethod(
        string assemblyName,
        TypeDefinition type,
        MethodDefinition method,
        ICollection<DllStringLiteral> result)
    {
        var instructions = method.Body.Instructions;
        for (var index = 0; index < instructions.Count; index++)
        {
            var instruction = instructions[index];
            if (instruction.OpCode.Code != Code.Newobj)
            {
                continue;
            }

            if (instruction.Operand is not MethodReference ctor ||
                !IsTextObjectCtor(ctor) ||
                ctor.Parameters.Count < 1 ||
                !IsStringType(ctor.Parameters[0].ParameterType))
            {
                continue;
            }

            if (!TryExtractFirstArgumentStringLiteral(instructions, index, ctor.Parameters.Count, out var literal))
            {
                continue;
            }

            var normalized = TextEncodingNormalizer.NormalizeMojibake(literal.Trim());
            if (string.IsNullOrWhiteSpace(normalized))
            {
                continue;
            }

            result.Add(new DllStringLiteral(
                assemblyName,
                type.FullName,
                method.Name,
                normalized));
        }
    }

    private static bool IsTextObjectCtor(MethodReference ctor)
    {
        return ctor.Name == ".ctor" &&
               ctor.DeclaringType is not null &&
               ctor.DeclaringType.FullName == "TaleWorlds.Localization.TextObject";
    }

    private static bool IsStringType(TypeReference type)
    {
        return type.FullName == "System.String";
    }

    /// <summary>
    /// 尝试从 <c>newobj</c> 之前的压栈指令中，提取“第一个参数”的字符串字面量。
    /// 保守实现：仅允许参数压栈指令为“无 pop、push=1”的简单指令序列；遇到复杂表达式直接放弃。
    /// </summary>
    private static bool TryExtractFirstArgumentStringLiteral(
        Mono.Collections.Generic.Collection<Instruction> instructions,
        int newobjIndex,
        int parameterCount,
        out string literal)
    {
        literal = string.Empty;
        if (parameterCount <= 0)
        {
            return false;
        }

        var needed = parameterCount;
        for (var i = newobjIndex - 1; i >= 0 && needed > 0; i--)
        {
            var inst = instructions[i];
            if (inst.OpCode.Code == Code.Nop)
            {
                continue;
            }

            if (!TryGetSimplePushOnePopZero(inst, out var pushed))
            {
                return false;
            }

            if (pushed != 1)
            {
                return false;
            }

            needed--;
            if (needed == 0)
            {
                if (inst.OpCode.Code != Code.Ldstr)
                {
                    return false;
                }

                literal = inst.Operand?.ToString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(literal);
            }
        }

        return false;
    }

    private static bool TryGetSimplePushOnePopZero(Instruction instruction, out int pushed)
    {
        pushed = 0;

        // 仅允许“无 pop、push=1”的简单压栈指令。
        // 这样可以保守地定位 newobj 的参数压栈序列，并避免复杂表达式导致误判。
        switch (instruction.OpCode.Code)
        {
            case Code.Ldstr:
            case Code.Ldnull:
            case Code.Ldc_I4:
            case Code.Ldc_I4_0:
            case Code.Ldc_I4_1:
            case Code.Ldc_I4_2:
            case Code.Ldc_I4_3:
            case Code.Ldc_I4_4:
            case Code.Ldc_I4_5:
            case Code.Ldc_I4_6:
            case Code.Ldc_I4_7:
            case Code.Ldc_I4_8:
            case Code.Ldc_I4_M1:
            case Code.Ldc_I4_S:
            case Code.Ldc_I8:
            case Code.Ldc_R4:
            case Code.Ldc_R8:
            case Code.Ldloc:
            case Code.Ldloc_0:
            case Code.Ldloc_1:
            case Code.Ldloc_2:
            case Code.Ldloc_3:
            case Code.Ldloc_S:
            case Code.Ldloca:
            case Code.Ldloca_S:
            case Code.Ldarg:
            case Code.Ldarg_0:
            case Code.Ldarg_1:
            case Code.Ldarg_2:
            case Code.Ldarg_3:
            case Code.Ldarg_S:
            case Code.Ldarga:
            case Code.Ldarga_S:
                pushed = 1;
                return true;
            default:
                return false;
        }
    }
}

