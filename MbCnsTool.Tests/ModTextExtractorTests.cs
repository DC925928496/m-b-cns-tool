using MbCnsTool.Core.Extraction;
using MbCnsTool.Core.Services;

namespace MbCnsTool.Tests;

/// <summary>
/// 提取器测试。
/// </summary>
public sealed class ModTextExtractorTests
{
    [Fact]
    public async Task Extract_Should_Read_Xml_And_Json_Text()
    {
        var root = Path.Combine(Path.GetTempPath(), $"mod-{Guid.NewGuid():N}");
        var moduleRoot = Path.Combine(root, "DemoMod");
        Directory.CreateDirectory(moduleRoot);
        await File.WriteAllTextAsync(Path.Combine(moduleRoot, "SubModule.xml"), "<Module></Module>");
        Directory.CreateDirectory(Path.Combine(moduleRoot, "ModuleData", "Languages"));
        Directory.CreateDirectory(Path.Combine(moduleRoot, "ModuleData", "Enlisted", "Dialogue"));

        await File.WriteAllTextAsync(
            Path.Combine(moduleRoot, "ModuleData", "Languages", "strings.xml"),
            """
            <base>
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
                  "text": "Stores are full. What do you need?"
                }
              ]
            }
            """);

        try
        {
            var extractor = new ModTextExtractor(new TextClassifier(), new DllStringScanner());
            var bundle = extractor.Extract(root);

            Assert.Equal(moduleRoot, bundle.ModuleRootPath);
            Assert.True(bundle.TextUnits.Count >= 2);
            Assert.NotEmpty(bundle.Documents);
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
    public async Task Extract_Should_Skip_AtPrefixed_Text_And_Parse_TranslationId()
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
            var extractor = new ModTextExtractor(new TextClassifier(), new DllStringScanner());
            var bundle = extractor.Extract(root);
            var units = bundle.TextUnits.Where(unit => unit.RelativePath.EndsWith("items.xml", StringComparison.OrdinalIgnoreCase)).ToArray();

            Assert.DoesNotContain(units, unit => unit.SourceText.StartsWith('@'));
            var interfaceUnit = Assert.Single(units.Where(unit => unit.SourceText.Contains("{=demo_item_name}", StringComparison.Ordinal)));
            Assert.Equal("demo_item_name", interfaceUnit.TranslationId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
