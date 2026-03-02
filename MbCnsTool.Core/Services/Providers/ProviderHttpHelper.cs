using System.Net;
using System.Security.Authentication;

namespace MbCnsTool.Core.Services.Providers;

/// <summary>
/// 提供方 HTTP 调用重试策略。
/// </summary>
public static class ProviderHttpHelper
{
    /// <summary>
    /// 执行带重试的 HTTP 请求。
    /// </summary>
    public static async Task<T?> SendWithRetryAsync<T>(
        Func<CancellationToken, Task<(HttpStatusCode StatusCode, T? Value)>> sender,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 4;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                var (statusCode, value) = await sender(cancellationToken);
                if ((int)statusCode is >= 200 and < 300)
                {
                    return value;
                }

                if (statusCode == HttpStatusCode.TooManyRequests && attempt < maxAttempts - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(20), cancellationToken);
                    continue;
                }

                if ((int)statusCode >= 500 && attempt < maxAttempts - 1)
                {
                    await Task.Delay(TimeSpan.FromSeconds(GetBackoffSeconds(attempt)), cancellationToken);
                    continue;
                }
            }
            catch (TaskCanceledException) when (attempt < maxAttempts - 1 && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(GetBackoffSeconds(attempt)), cancellationToken);
            }
            catch (HttpRequestException) when (attempt < maxAttempts - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(GetBackoffSeconds(attempt)), cancellationToken);
            }
            catch (IOException) when (attempt < maxAttempts - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(GetBackoffSeconds(attempt)), cancellationToken);
            }
            catch (AuthenticationException) when (attempt < maxAttempts - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(GetBackoffSeconds(attempt)), cancellationToken);
            }
        }

        return default;
    }

    private static int GetBackoffSeconds(int attempt)
    {
        return attempt switch
        {
            <= 0 => 2,
            1 => 4,
            _ => 8
        };
    }
}
