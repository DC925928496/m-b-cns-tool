using System.Xml.Linq;
using MbCnsTool.Core.Extraction;
using MbCnsTool.Core.Models;
using MbCnsTool.Core.Packaging;

namespace MbCnsTool.Tests;

/// <summary>
/// 汉化包构建测试。
/// </summary>
public sealed class PackageBuilderTests
{
    [Fact]
    public async Task BuildAsync_External_Should_Preserve_Xmls_In_SubModule()
    {
        var root = Path.Combine(Path.GetTempPath(), $"package-{Guid.NewGuid():N}");
        var moduleRoot = Path.Combine(root, "DemoMod");
        var outputRoot = Path.Combine(root, "output");
        Directory.CreateDirectory(moduleRoot);

        await File.WriteAllTextAsync(
            Path.Combine(moduleRoot, "SubModule.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Module>
              <Name value="Demo Mod" />
              <Id value="DemoMod" />
              <Version value="v1.0.0" />
              <DefaultModule value="false" />
              <SingleplayerModule value="true" />
              <MultiplayerModule value="false" />
              <Official value="false" />
              <DependedModules>
                <DependedModule Id="Native" />
              </DependedModules>
              <SubModules>
                <SubModule>
                  <Name value="Demo.SubModule" />
                </SubModule>
              </SubModules>
              <Xmls>
                <XmlNode>
                  <XmlName id="LanguagesData" path="ModuleData/Languages/language_data.xml" />
                </XmlNode>
              </Xmls>
            </Module>
            """);

        try
        {
            var builder = new PackageBuilder();
            var bundle = new ScanBundle
            {
                ModuleRootPath = moduleRoot,
                Documents = [],
                TextUnits = [],
                DllLiterals = []
            };
            var options = new TranslationRunOptions
            {
                ModPath = moduleRoot,
                OutputPath = outputRoot,
                Mode = "external"
            };

            var (outputPath, _) = await builder.BuildAsync(bundle, new Dictionary<string, string>(), options, CancellationToken.None);
            var outputSubModulePath = Path.Combine(outputPath, "SubModule.xml");
            var outputDocument = XDocument.Load(outputSubModulePath);
            var module = outputDocument.Root;

            Assert.NotNull(module);
            Assert.Equal("DemoMod_CNs", module!.Elements().First(element => element.Name.LocalName == "Name").Attribute("value")?.Value);
            Assert.Equal("DemoMod_CNs", module.Elements().First(element => element.Name.LocalName == "Id").Attribute("value")?.Value);
            Assert.Equal("DemoMod", module
                .Elements()
                .First(element => element.Name.LocalName == "DependedModules")
                .Elements()
                .First(element => element.Name.LocalName == "DependedModule")
                .Attribute("Id")
                ?.Value);

            Assert.NotNull(module.Elements().FirstOrDefault(element => element.Name.LocalName == "Xmls"));
            Assert.Empty(module
                .Elements()
                .First(element => element.Name.LocalName == "SubModules")
                .Elements());
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
    public async Task BuildAsync_External_Should_Create_CNs_LanguageData()
    {
        var root = Path.Combine(Path.GetTempPath(), $"package-{Guid.NewGuid():N}");
        var moduleRoot = Path.Combine(root, "DemoMod");
        var outputRoot = Path.Combine(root, "output");
        Directory.CreateDirectory(moduleRoot);

        await File.WriteAllTextAsync(
            Path.Combine(moduleRoot, "SubModule.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Module>
              <Name value="Demo Mod" />
              <Id value="DemoMod" />
              <Version value="v1.0.0" />
              <DefaultModule value="false" />
              <SingleplayerModule value="true" />
              <MultiplayerModule value="false" />
              <Official value="false" />
              <DependedModules>
                <DependedModule Id="Native" />
              </DependedModules>
              <SubModules />
              <Xmls>
                <XmlNode>
                  <XmlName id="LanguagesData" path="ModuleData/Languages/language_data.xml" />
                </XmlNode>
              </Xmls>
            </Module>
            """);

        try
        {
            var languageXml = XDocument.Parse(
                """
                <base type="string">
                  <tags>
                    <tag language="English" />
                  </tags>
                  <strings>
                    <string id="demo_01" text="Hello" />
                  </strings>
                </base>
                """);
            var builder = new PackageBuilder();
            var bundle = new ScanBundle
            {
                ModuleRootPath = moduleRoot,
                Documents =
                [
                    new XmlSourceDocument
                    {
                        RelativePath = Path.Combine("ModuleData", "Languages", "std_module_strings_xml.xml"),
                        Document = languageXml
                    }
                ],
                TextUnits = [],
                DllLiterals = []
            };
            var options = new TranslationRunOptions
            {
                ModPath = moduleRoot,
                OutputPath = outputRoot,
                Mode = "external",
                TargetLanguage = "zh-CN"
            };

            var (outputPath, _) = await builder.BuildAsync(bundle, new Dictionary<string, string>(), options, CancellationToken.None);
            var languageDataPath = Path.Combine(outputPath, "ModuleData", "Languages", "CNs", "language_data.xml");
            var languageXmlPath = Path.Combine(outputPath, "ModuleData", "Languages", "CNs", "std_module_strings_xml.xml");

            Assert.True(File.Exists(languageDataPath));
            Assert.True(File.Exists(languageXmlPath));

            var languageData = XDocument.Load(languageDataPath);
            var languageDataRoot = languageData.Root;
            Assert.NotNull(languageDataRoot);
            Assert.Equal("简体中文", languageDataRoot!.Attribute("id")?.Value);
            Assert.Equal("false", languageDataRoot.Attribute("under_development")?.Value);
            Assert.Equal("CNs/std_module_strings_xml.xml", languageDataRoot
                .Elements()
                .First(element => element.Name.LocalName == "LanguageFile")
                .Attribute("xml_path")
                ?.Value);

            var normalizedLanguage = XDocument.Load(languageXmlPath);
            Assert.Equal("简体中文", normalizedLanguage
                .Descendants()
                .First(element => element.Name.LocalName == "tag")
                .Attribute("language")
                ?.Value);
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
    public async Task BuildAsync_External_Should_Generate_LanguageFile_From_InterfaceText_Only()
    {
        var root = Path.Combine(Path.GetTempPath(), $"package-{Guid.NewGuid():N}");
        var moduleRoot = Path.Combine(root, "DemoMod");
        var outputRoot = Path.Combine(root, "output");
        Directory.CreateDirectory(moduleRoot);

        await File.WriteAllTextAsync(
            Path.Combine(moduleRoot, "SubModule.xml"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <Module>
              <Name value="Demo Mod" />
              <Id value="DemoMod" />
              <Version value="v1.0.0" />
              <DefaultModule value="false" />
              <SingleplayerModule value="true" />
              <MultiplayerModule value="false" />
              <Official value="false" />
              <DependedModules>
                <DependedModule Id="Native" />
              </DependedModules>
              <SubModules />
              <Xmls>
                <XmlNode>
                  <XmlName id="Items" path="ModuleData/items.xml" />
                </XmlNode>
              </Xmls>
            </Module>
            """);

        try
        {
            var builder = new PackageBuilder();
            var bundle = new ScanBundle
            {
                ModuleRootPath = moduleRoot,
                Documents =
                [
                    new XmlSourceDocument
                    {
                        RelativePath = Path.Combine("ModuleData", "items.xml"),
                        Document = XDocument.Parse(
                            """
                            <Items>
                              <Item id="demo_item" text="{=demo_item_name}Iron Sword" />
                            </Items>
                            """)
                    }
                ],
                TextUnits =
                [
                    new TextUnit
                    {
                        Id = "1",
                        RelativePath = Path.Combine("ModuleData", "items.xml"),
                        FieldPath = "/Items[0]/Item[0].@text",
                        SourceText = "{=demo_item_name}Iron Sword",
                        Category = TextCategory.物品,
                        KeyName = "text",
                        TranslationId = "demo_item_name",
                        ApplyTranslation = _ => { },
                        ReadCurrentText = () => "{=demo_item_name}铁剑"
                    }
                ],
                DllLiterals = []
            };
            var options = new TranslationRunOptions
            {
                ModPath = moduleRoot,
                OutputPath = outputRoot,
                Mode = "external",
                TargetLanguage = "zh-CN"
            };

            var (outputPath, _) = await builder.BuildAsync(bundle, new Dictionary<string, string>(), options, CancellationToken.None);
            var copiedItemPath = Path.Combine(outputPath, "ModuleData", "items.xml");
            var patchPath = Path.Combine(outputPath, "ModuleData", "items.xslt");
            var languageXmlPath = Path.Combine(outputPath, "ModuleData", "Languages", "CNs", "std_module_strings_xml.xml");
            var languageDataRootPath = Path.Combine(outputPath, "ModuleData", "Languages", "language_data.xml");

            Assert.False(File.Exists(copiedItemPath));
            Assert.False(File.Exists(patchPath));
            Assert.True(File.Exists(languageXmlPath));
            Assert.True(File.Exists(languageDataRootPath));

            var languageXml = XDocument.Load(languageXmlPath);
            var translatedNode = languageXml
                .Descendants()
                .First(element => element.Name.LocalName == "string" && element.Attribute("id")?.Value == "demo_item_name");
            Assert.Equal("铁剑", translatedNode.Attribute("text")?.Value);
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
