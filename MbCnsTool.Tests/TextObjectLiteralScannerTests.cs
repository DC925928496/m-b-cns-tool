using MbCnsTool.Core.Extraction;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MbCnsTool.Tests;

/// <summary>
/// TextObject 字面量扫描测试。
/// </summary>
public sealed class TextObjectLiteralScannerTests
{
    [Fact]
    public async Task ScanTextObjectStringLiterals_Should_Only_Return_Literals_Passed_To_TextObject_Ctor()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mod-{Guid.NewGuid():N}");
        var moduleRoot = Path.Combine(root, "DemoMod");
        Directory.CreateDirectory(moduleRoot);

        await File.WriteAllTextAsync(
            Path.Combine(moduleRoot, "SubModule.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Module>
              <SubModules>
                <SubModule>
                  <Name value="Demo.SubModule" />
                  <DLLName value="DemoMod.dll" />
                </SubModule>
              </SubModules>
            </Module>
            """);

        var binRoot = Path.Combine(moduleRoot, "bin", "Win64_Shipping_Client");
        Directory.CreateDirectory(binRoot);
        var dllPath = Path.Combine(binRoot, "DemoMod.dll");
        CreateDllWithTextObjectCtorLiteral(dllPath);

        try
        {
            var scanner = new TextObjectLiteralScanner();
            var literals = scanner.ScanTextObjectStringLiterals(moduleRoot);

            Assert.Contains(literals, literal =>
                literal.SourceText == "Hardcoded" &&
                literal.AssemblyName == "DemoMod.dll");

            Assert.DoesNotContain(literals, literal => literal.SourceText == "NotUsed");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void CreateDllWithTextObjectCtorLiteral(string dllPath)
    {
        var assemblyName = new AssemblyNameDefinition(Path.GetFileNameWithoutExtension(dllPath), new Version(1, 0, 0, 0));
        var assembly = AssemblyDefinition.CreateAssembly(assemblyName, assemblyName.Name, ModuleKind.Dll);
        var module = assembly.MainModule;

        // 定义 TaleWorlds.Localization.TextObject 以便扫描器命中目标类型。
        var textObjectType = new TypeDefinition(
            "TaleWorlds.Localization",
            "TextObject",
            TypeAttributes.Public | TypeAttributes.Class,
            module.TypeSystem.Object);
        module.Types.Add(textObjectType);

        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        ctor.Parameters.Add(new ParameterDefinition("value", ParameterAttributes.None, module.TypeSystem.String));
        textObjectType.Methods.Add(ctor);
        var ctorIl = ctor.Body.GetILProcessor();
        ctorIl.Append(ctorIl.Create(OpCodes.Ret));

        var demoType = new TypeDefinition(
            "Demo",
            "TestType",
            TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed,
            module.TypeSystem.Object);
        module.Types.Add(demoType);

        var method = new MethodDefinition(
            "Touch",
            MethodAttributes.Public | MethodAttributes.Static,
            module.TypeSystem.Void);
        demoType.Methods.Add(method);

        var il = method.Body.GetILProcessor();
        il.Append(il.Create(OpCodes.Ldstr, "Hardcoded"));
        il.Append(il.Create(OpCodes.Newobj, ctor));
        il.Append(il.Create(OpCodes.Pop));
        il.Append(il.Create(OpCodes.Ldstr, "NotUsed"));
        il.Append(il.Create(OpCodes.Pop));
        il.Append(il.Create(OpCodes.Ret));

        assembly.Write(dllPath);
    }
}

