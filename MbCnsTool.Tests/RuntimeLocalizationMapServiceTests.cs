using System.Text;
using MbCnsTool.Core.Models;
using MbCnsTool.Core.Services;

namespace MbCnsTool.Tests;

/// <summary>
/// runtime_localization.json 读写测试。
/// </summary>
public sealed class RuntimeLocalizationMapServiceTests
{
    [Fact]
    public async Task SaveAndLoad_Should_Preserve_Newlines_And_Fill_Base64()
    {
        var root = Path.Combine(Path.GetTempPath(), $"runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "runtime_localization.json");

        try
        {
            var map = new RuntimeLocalizationMap
            {
                TargetLanguage = "zh-CN",
                Entries =
                [
                    new RuntimeLocalizationEntry
                    {
                        Id = "auto_demo",
                        SourceText = "Line1\nLine2",
                        SourceTextBase64 = string.Empty,
                        TargetText = "第一行\n第二行"
                    }
                ]
            };

            var service = new RuntimeLocalizationMapService();
            await service.SaveAsync(path, map, CancellationToken.None);

            var loaded = await service.TryLoadAsync(path, CancellationToken.None);
            Assert.NotNull(loaded);
            Assert.Equal("zh-CN", loaded!.TargetLanguage);
            Assert.Single(loaded.Entries);

            var entry = loaded.Entries[0];
            Assert.Equal("Line1\nLine2", entry.SourceText);
            Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("Line1\nLine2")), entry.SourceTextBase64);
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

