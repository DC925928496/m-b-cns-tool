using MbCnsTool.Core.Services;

namespace MbCnsTool.Tests;

/// <summary>
/// 术语表测试。
/// </summary>
public sealed class GlossaryServiceTests
{
    [Fact]
    public async Task Load_And_Apply_Should_Work()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"glossary-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(tempFile, "Denar=第纳尔\nBannerlord=霸主");

        try
        {
            var service = await GlossaryService.LoadAsync(tempFile, CancellationToken.None);
            var translated = service.ApplyToTranslation("You received Denar in Bannerlord.");

            Assert.Contains("第纳尔", translated);
            Assert.Contains("霸主", translated);
            Assert.NotEmpty(service.BuildPrompt());
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }
}
