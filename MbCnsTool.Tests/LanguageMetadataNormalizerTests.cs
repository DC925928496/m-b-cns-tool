using System.Xml.Linq;
using MbCnsTool.Core.Extraction;
using MbCnsTool.Core.Models;
using MbCnsTool.Core.Services;

namespace MbCnsTool.Tests;

/// <summary>
/// 语言元数据规范化测试。
/// </summary>
public sealed class LanguageMetadataNormalizerTests
{
    [Fact]
    public void Normalize_ZhCn_Should_Use_SimplifiedChinese_Metadata()
    {
        var languageData = XDocument.Parse("""<LanguageData id="English" name="English"></LanguageData>""");
        var strings = XDocument.Parse(
            """
            <base>
              <tags>
                <tag language="English" />
              </tags>
              <strings>
                <string id="id_1" text="Hello" />
              </strings>
            </base>
            """);

        var documents = new SourceDocument[]
        {
            new XmlSourceDocument
            {
                RelativePath = Path.Combine("ModuleData", "Languages", "language_data.xml"),
                Document = languageData
            },
            new XmlSourceDocument
            {
                RelativePath = Path.Combine("ModuleData", "Languages", "std_module_strings_xml.xml"),
                Document = strings
            }
        };

        var normalizer = new LanguageMetadataNormalizer();
        normalizer.Normalize(documents, "zh-CN");

        var root = languageData.Root;
        Assert.NotNull(root);
        Assert.Equal("简体中文", root!.Attribute("id")?.Value);
        Assert.Equal("简体中文", root.Attribute("name")?.Value);
        Assert.Equal("CN", root.Attribute("subtitle_extension")?.Value);
        Assert.Equal("zh,zh-CN", root.Attribute("supported_iso")?.Value);
        Assert.Equal("false", root.Attribute("under_development")?.Value);

        var tag = strings
            .Descendants()
            .First(element => element.Name.LocalName == "tag");
        Assert.Equal("简体中文", tag.Attribute("language")?.Value);
    }
}
