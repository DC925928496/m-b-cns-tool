using MbCnsTool.Core.Models;
using MbCnsTool.Core.Services;

namespace MbCnsTool.Tests;

/// <summary>
/// 翻译对比服务测试。
/// </summary>
public sealed class TranslationReviewServiceTests
{
    [Fact]
    public async Task Save_Then_Load_Should_Preserve_Entries()
    {
        var root = Path.Combine(Path.GetTempPath(), $"review-{Guid.NewGuid():N}");
        var filePath = Path.Combine(root, "translation_review.json");
        var snapshot = new TranslationReviewSnapshot
        {
            ModuleName = "DemoMod",
            StyleProfile = "style",
            TargetLanguage = "zh-CN",
            GeneratedAtUtc = DateTimeOffset.Parse("2026-03-03T00:00:00Z"),
            Entries =
            [
                new TranslationReviewEntry
                {
                    CacheKey = "k1",
                    Category = "物品",
                    RelativePath = "ModuleData/items.xml",
                    FieldPath = "/Items[0]/Item[0].@name",
                    SourceText = "Iron Sword",
                    TargetText = "铁剑",
                    IsDllLiteral = false
                }
            ]
        };
        var service = new TranslationReviewService();

        try
        {
            await service.SaveAsync(filePath, snapshot, CancellationToken.None);
            var loaded = await service.TryLoadAsync(filePath, CancellationToken.None);

            Assert.NotNull(loaded);
            Assert.Equal("DemoMod", loaded!.ModuleName);
            Assert.Single(loaded.Entries);
            Assert.Equal("铁剑", loaded.Entries[0].TargetText);
            Assert.Equal("k1", loaded.Entries[0].CacheKey);
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
    public async Task ApplySnapshotToCache_Should_Only_Write_NonEmpty_Target()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cache-{Guid.NewGuid():N}.db");
        var snapshot = new TranslationReviewSnapshot
        {
            ModuleName = "DemoMod",
            StyleProfile = "style",
            TargetLanguage = "zh-CN",
            GeneratedAtUtc = DateTimeOffset.UtcNow,
            Entries =
            [
                new TranslationReviewEntry
                {
                    CacheKey = "k1",
                    Category = "通用",
                    RelativePath = "a.xml",
                    FieldPath = "/a",
                    SourceText = "hello",
                    TargetText = "你好",
                    IsDllLiteral = false
                },
                new TranslationReviewEntry
                {
                    CacheKey = "k2",
                    Category = "通用",
                    RelativePath = "a.xml",
                    FieldPath = "/b",
                    SourceText = "world",
                    TargetText = " ",
                    IsDllLiteral = false
                },
                new TranslationReviewEntry
                {
                    CacheKey = "k3",
                    Category = "通用",
                    RelativePath = "a.xml",
                    FieldPath = "/c",
                    SourceText = "{=enl_retinue_select_type_prompt}Select a soldier type to begin.",
                    TargetText = "{=enl_雷特inue_select_type_prompt}选择一个士兵类型开始。",
                    IsDllLiteral = false
                }
            ]
        };
        var service = new TranslationReviewService();

        await using var cache = await TranslationCache.OpenAsync(dbPath, CancellationToken.None);
        var updated = await service.ApplySnapshotToCacheAsync(snapshot, cache, CancellationToken.None);
        var k1 = await cache.TryGetAsync("k1", CancellationToken.None);
        var k2 = await cache.TryGetAsync("k2", CancellationToken.None);
        var k3 = await cache.TryGetAsync("k3", CancellationToken.None);

        Assert.Equal(1, updated);
        Assert.Equal("你好", k1);
        Assert.Null(k2);
        Assert.Null(k3);
    }
}
