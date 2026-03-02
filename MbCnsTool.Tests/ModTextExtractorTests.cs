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
}
