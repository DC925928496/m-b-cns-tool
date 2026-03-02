using MbCnsTool.Core.Services;

namespace MbCnsTool.Tests;

/// <summary>
/// 缓存测试。
/// </summary>
public sealed class TranslationCacheTests
{
    [Fact]
    public async Task Upsert_And_Get_Should_Work()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cache-{Guid.NewGuid():N}.db");
        await using (var cache = await TranslationCache.OpenAsync(dbPath, CancellationToken.None))
        {
            await cache.UpsertAsync("k1", "provider", "hello", "你好", CancellationToken.None);

            var value = await cache.TryGetAsync("k1", CancellationToken.None);
            Assert.Equal("你好", value);
        }

    }

    [Fact]
    public async Task Concurrent_Upsert_And_Get_Should_Work()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"cache-{Guid.NewGuid():N}.db");
        await using (var cache = await TranslationCache.OpenAsync(dbPath, CancellationToken.None))
        {
            var tasks = Enumerable.Range(0, 30).Select(async index =>
            {
                var key = $"k{index}";
                var value = $"值{index}";
                await cache.UpsertAsync(key, "provider", $"src-{index}", value, CancellationToken.None);
                var loaded = await cache.TryGetAsync(key, CancellationToken.None);
                Assert.Equal(value, loaded);
            });

            await Task.WhenAll(tasks);
        }

    }
}
