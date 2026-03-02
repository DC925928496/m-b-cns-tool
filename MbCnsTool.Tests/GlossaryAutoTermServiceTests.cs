using MbCnsTool.Core.Models;
using MbCnsTool.Core.Services;

namespace MbCnsTool.Tests;

/// <summary>
/// 高频词自动入术语表测试。
/// </summary>
public sealed class GlossaryAutoTermServiceTests
{
    [Fact]
    public async Task AppendFrequentTerms_Should_Add_Translated_Terms()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auto-term-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "Denar=第纳尔\n");

        var unit = new TextUnit
        {
            Id = "1",
            RelativePath = "a.json",
            FieldPath = "$.text",
            SourceText = "Quartermaster Quartermaster Quartermaster Quartermaster Quartermaster",
            Category = TextCategory.对话,
            KeyName = "text",
            ApplyTranslation = _ => { }
        };

        try
        {
            var added = await GlossaryAutoTermService.AppendFrequentTermsAsync(
                path,
                [unit],
                CancellationToken.None,
                termTranslator: (source, _) => Task.FromResult<string?>(source.Equals("quartermaster", StringComparison.OrdinalIgnoreCase) ? "军需官" : null),
                minFrequency: 3,
                maxTerms: 10);

            var lines = await File.ReadAllLinesAsync(path);
            Assert.True(added >= 1);
            Assert.Contains(lines, line => line.StartsWith("quartermaster=军需官", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(lines, line => line.StartsWith("quartermaster=quartermaster", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task AppendFrequentTerms_Should_Skip_EnglishToEnglish_Result()
    {
        var path = Path.Combine(Path.GetTempPath(), $"auto-term-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "# 自动术语表\n");

        var unit = new TextUnit
        {
            Id = "2",
            RelativePath = "b.json",
            FieldPath = "$.name",
            SourceText = "Mercenary Mercenary Mercenary Mercenary",
            Category = TextCategory.物品,
            KeyName = "name",
            ApplyTranslation = _ => { }
        };

        try
        {
            var added = await GlossaryAutoTermService.AppendFrequentTermsAsync(
                path,
                [unit],
                CancellationToken.None,
                termTranslator: (_, _) => Task.FromResult<string?>("mercenary"),
                minFrequency: 2,
                maxTerms: 10);

            var lines = await File.ReadAllLinesAsync(path);
            Assert.Equal(0, added);
            Assert.DoesNotContain(lines, line => line.StartsWith("mercenary=", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
