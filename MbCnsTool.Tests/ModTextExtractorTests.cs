using MbCnsTool.Core.Extraction;
using MbCnsTool.Core.Services;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MbCnsTool.Tests;

/// <summary>
/// 提取器测试。
/// </summary>
public sealed class ModTextExtractorTests
{
    [Fact]
    public async Task Extract_Strict_Should_Only_Translate_LanguageFile_And_Collect_TranslationIds_From_Json()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mod-{Guid.NewGuid():N}");
        var moduleRoot = Path.Combine(root, "DemoMod");
        Directory.CreateDirectory(moduleRoot);
        await File.WriteAllTextAsync(Path.Combine(moduleRoot, "SubModule.xml"), "<Module></Module>");
        Directory.CreateDirectory(Path.Combine(moduleRoot, "ModuleData", "Languages"));
        Directory.CreateDirectory(Path.Combine(moduleRoot, "ModuleData", "Enlisted", "Dialogue"));

        await File.WriteAllTextAsync(
            Path.Combine(moduleRoot, "ModuleData", "Languages", "std_module_strings_xml.xml"),
            """
            <base type="string">
              <tags>
                <tag language="English" />
              </tags>
              <strings>
                <string id="s1" text="Promotion available!" />
              </strings>
            </base>
            """);

        await File.WriteAllTextAsync(
            Path.Combine(moduleRoot, "ModuleData", "Enlisted", "Dialogue", "dialogue.json"),
            """
            {
              "nodes": [
                {
                  "text": "{=dlg_1}Stores are full. What do you need?",
                  "debug_note": "This is not translated in strict mode."
                },
                {
                  "text": "Stores are full. What do you need?"
                }
              ]
            }
            """);

        try
        {
            var extractor = new ModTextExtractor(new TextClassifier(), new TextObjectLiteralScanner());
            var bundle = extractor.Extract(root);

            Assert.Equal(moduleRoot, bundle.ModuleRootPath);
            Assert.Empty(bundle.DllLiterals);

            Assert.Contains(bundle.TextUnits, unit => unit.TranslationId == "s1" && unit.SourceText == "Promotion available!");
            Assert.Contains(bundle.TextUnits, unit => unit.TranslationId == "dlg_1" && unit.SourceText == "Stores are full. What do you need?");
            Assert.DoesNotContain(bundle.TextUnits, unit => unit.SourceText.Contains("debug_note", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(bundle.TextUnits, unit => unit.SourceText == "This is not translated in strict mode.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Extract_Strict_Should_Create_LanguageFile_And_Collect_TranslationId_From_Xml()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mod-{Guid.NewGuid():N}");
        var moduleRoot = Path.Combine(root, "DemoMod");
        Directory.CreateDirectory(moduleRoot);
        await File.WriteAllTextAsync(Path.Combine(moduleRoot, "SubModule.xml"), "<Module></Module>");
        Directory.CreateDirectory(Path.Combine(moduleRoot, "ModuleData"));
        await File.WriteAllTextAsync(
            Path.Combine(moduleRoot, "ModuleData", "items.xml"),
            """
            <Items>
              <Item id="demo_item" name="@item_key" text="{=demo_item_name}Iron Sword" />
            </Items>
            """);

        try
        {
            var extractor = new ModTextExtractor(new TextClassifier(), new TextObjectLiteralScanner());
            var bundle = extractor.Extract(root);

            Assert.Empty(bundle.DllLiterals);
            Assert.Contains(bundle.TextUnits, unit => unit.TranslationId == "demo_item_name" && unit.SourceText == "Iron Sword");
            Assert.DoesNotContain(bundle.TextUnits, unit => unit.SourceText.StartsWith('@'));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Extract_Strict_Should_Normalize_Mojibake_DefaultText_When_Collecting_TranslationId()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mod-{Guid.NewGuid():N}");
        var moduleRoot = Path.Combine(root, "DemoMod");
        Directory.CreateDirectory(moduleRoot);
        await File.WriteAllTextAsync(Path.Combine(moduleRoot, "SubModule.xml"), "<Module></Module>");
        Directory.CreateDirectory(Path.Combine(moduleRoot, "ModuleData"));
        await File.WriteAllTextAsync(
            Path.Combine(moduleRoot, "ModuleData", "menu.xml"),
            """
            <Menu>
              <Line text="{=dismiss_tip}â€” Dismiss Soldiers â€”" />
            </Menu>
            """);

        try
        {
            var extractor = new ModTextExtractor(new TextClassifier(), new TextObjectLiteralScanner());
            var bundle = extractor.Extract(root);
            Assert.Contains(bundle.TextUnits, unit => unit.TranslationId == "dismiss_tip" && unit.SourceText == "— Dismiss Soldiers —");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Extract_Strict_Should_Scan_TextObject_Literals_And_Deduplicate_Existing_Ids()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mod-{Guid.NewGuid():N}");
        var moduleRoot = Path.Combine(root, "DemoMod");
        Directory.CreateDirectory(moduleRoot);
        var languageRoot = Path.Combine(moduleRoot, "ModuleData", "Languages");
        var cnsRoot = Path.Combine(languageRoot, "CNs");
        Directory.CreateDirectory(cnsRoot);

        await File.WriteAllTextAsync(
            Path.Combine(languageRoot, "language_data.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <LanguageData id="简体中文">
              <LanguageFile xml_path="CNs/std_module_strings_CNs.xml" />
            </LanguageData>
            """);

        await File.WriteAllTextAsync(
            Path.Combine(cnsRoot, "std_module_strings_CNs.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <base type="string">
              <strings>
                <string id="exists" text="已存在" />
              </strings>
            </base>
            """);

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
        var declaredDllPath = Path.Combine(binRoot, "DemoMod.dll");
        var otherDllPath = Path.Combine(binRoot, "NotReferenced.dll");
        CreateDllWithTextObjectCtorCalls(declaredDllPath, "{=exists}Hello Exists", "{=dll_id}Hello From Dll", "Hardcoded");
        CreateDllWithTextObjectCtorCalls(otherDllPath, "{=other_id}Other", "OtherHardcoded");

        try
        {
            var extractor = new ModTextExtractor(new TextClassifier(), new TextObjectLiteralScanner());
            var bundle = extractor.Extract(root);

            Assert.Contains(bundle.TextUnits, unit => unit.TranslationId == "dll_id" && unit.SourceText == "Hello From Dll");
            Assert.DoesNotContain(bundle.TextUnits, unit => unit.TranslationId == "exists");
            Assert.DoesNotContain(bundle.TextUnits, unit => unit.TranslationId == "other_id");

            Assert.Contains(bundle.DllLiterals, literal => literal.SourceText == "Hardcoded" && literal.AssemblyName == "DemoMod.dll");
            Assert.DoesNotContain(bundle.DllLiterals, literal => literal.SourceText == "OtherHardcoded");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void CreateDllWithTextObjectCtorCalls(string dllPath, params string[] literals)
    {
        var assemblyName = new AssemblyNameDefinition(Path.GetFileNameWithoutExtension(dllPath), new Version(1, 0, 0, 0));
        var assembly = AssemblyDefinition.CreateAssembly(assemblyName, assemblyName.Name, ModuleKind.Dll);
        var module = assembly.MainModule;

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
        foreach (var literal in literals)
        {
            il.Append(il.Create(OpCodes.Ldstr, literal));
            il.Append(il.Create(OpCodes.Newobj, ctor));
            il.Append(il.Create(OpCodes.Pop));
        }
        il.Append(il.Create(OpCodes.Ret));

        assembly.Write(dllPath);
    }
}
