using System.Text;
using MbCnsTool.Core.Models;
using MbCnsTool.Core.Services;

namespace MbCnsTool.Tests;

/// <summary>
/// 翻译工程记录文件测试。
/// </summary>
public sealed class TranslationProjectServiceTests
{
    [Fact]
    public async Task SaveAndLoad_Should_Roundtrip_And_Fill_Base64()
    {
        var root = Path.Combine(Path.GetTempPath(), $"project-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "demo.mbcns_project.json");

        try
        {
            var project = new TranslationProject
            {
                ModuleId = "DemoMod",
                ModuleRootPath = root,
                TargetLanguage = "zh-CN",
                ScanDll = true,
                Entries =
                [
                    new TranslationProjectEntry
                    {
                        EntryKind = TranslationProjectEntryKind.LanguageString,
                        Category = "通用",
                        SourceFile = "ModuleData/Languages/std_module_strings_xml.xml",
                        Id = "demo_id",
                        SourceText = "Hello",
                        TargetText = "你好",
                        SourceTextBase64 = string.Empty
                    },
                    new TranslationProjectEntry
                    {
                        EntryKind = TranslationProjectEntryKind.DllTextObjectHardcoded,
                        Category = "系统",
                        SourceFile = "DemoMod.dll",
                        Id = "auto_x",
                        SourceText = "Line1\nLine2",
                        TargetText = "",
                        SourceTextBase64 = "bad"
                    }
                ]
            };

            var service = new TranslationProjectService();
            await service.SaveAsync(path, project, CancellationToken.None);

            var loaded = await service.TryLoadAsync(path, CancellationToken.None);
            Assert.NotNull(loaded);
            Assert.Equal("DemoMod", loaded!.ModuleId);
            Assert.Equal("zh-CN", loaded.TargetLanguage);
            Assert.True(loaded.ScanDll);
            Assert.Equal(2, loaded.Entries.Count);

            var entry0 = loaded.Entries[0];
            Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("Hello")), entry0.SourceTextBase64);

            var entry1 = loaded.Entries[1];
            Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("Line1\nLine2")), entry1.SourceTextBase64);
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
