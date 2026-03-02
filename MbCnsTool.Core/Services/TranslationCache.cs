using Microsoft.Data.Sqlite;

namespace MbCnsTool.Core.Services;

/// <summary>
/// SQLite 翻译缓存。
/// </summary>
public sealed class TranslationCache : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly SemaphoreSlim _mutex = new(1, 1);

    private TranslationCache(SqliteConnection connection)
    {
        _connection = connection;
    }

    /// <summary>
    /// 打开缓存数据库并初始化表结构。
    /// </summary>
    public static async Task<TranslationCache> OpenAsync(string dbPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath) ?? ".");
        var connection = new SqliteConnection($"Data Source={dbPath}");
        await connection.OpenAsync(cancellationToken);

        var initCommand = connection.CreateCommand();
        initCommand.CommandText = """
            CREATE TABLE IF NOT EXISTS translations (
              cache_key TEXT PRIMARY KEY,
              provider TEXT NOT NULL,
              source_text TEXT NOT NULL,
              target_text TEXT NOT NULL,
              updated_at TEXT NOT NULL
            );
            """;
        await initCommand.ExecuteNonQueryAsync(cancellationToken);

        return new TranslationCache(connection);
    }

    /// <summary>
    /// 查询缓存。
    /// </summary>
    public async Task<string?> TryGetAsync(string cacheKey, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var command = _connection.CreateCommand();
            command.CommandText = "SELECT target_text FROM translations WHERE cache_key = $cacheKey;";
            command.Parameters.AddWithValue("$cacheKey", cacheKey);
            var value = await command.ExecuteScalarAsync(cancellationToken);
            return value?.ToString();
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <summary>
    /// 写入缓存。
    /// </summary>
    public async Task UpsertAsync(string cacheKey, string providerName, string sourceText, string targetText, CancellationToken cancellationToken)
    {
        await _mutex.WaitAsync(cancellationToken);
        try
        {
            var command = _connection.CreateCommand();
            command.CommandText = """
                INSERT INTO translations(cache_key, provider, source_text, target_text, updated_at)
                VALUES($cacheKey, $provider, $source, $target, $updated)
                ON CONFLICT(cache_key) DO UPDATE SET
                  provider = excluded.provider,
                  source_text = excluded.source_text,
                  target_text = excluded.target_text,
                  updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$cacheKey", cacheKey);
            command.Parameters.AddWithValue("$provider", providerName);
            command.Parameters.AddWithValue("$source", sourceText);
            command.Parameters.AddWithValue("$target", targetText);
            command.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            _mutex.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        _mutex.Dispose();
    }
}
