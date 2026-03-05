using MbCnsTool.Core.Services;

namespace MbCnsTool.Tests;

/// <summary>
/// 语言文件索引构建测试。
/// </summary>
public sealed class LanguageStringIndexBuilderTests
{
    [Fact]
    public async Task Build_Should_Collect_All_Ids_And_Prefer_CNs_When_Defined()
    {
        var root = Path.Combine(Path.GetTempPath(), $"lang-{Guid.NewGuid():N}");
        var moduleRoot = Path.Combine(root, "DemoMod");
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
                <string id="a" text="A" />
              </strings>
            </base>
            """);

        await File.WriteAllTextAsync(
            Path.Combine(languageRoot, "std_module_strings_zho.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <base type="string">
              <strings>
                <string id="b" text="B" />
              </strings>
            </base>
            """);

        try
        {
            var builder = new LanguageStringIndexBuilder();
            var index = builder.Build(moduleRoot, "zh-CN");
            Assert.Equal("CNs", index.PreferredFolderCode);
            Assert.Contains("a", index.AllIds);
            Assert.Contains("b", index.AllIds);
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

