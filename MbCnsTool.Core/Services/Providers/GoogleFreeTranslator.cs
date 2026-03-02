using System.Net;
using System.Text.Json;
using MbCnsTool.Core.Abstractions;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Services.Providers;

/// <summary>
/// 基于 Google 非密钥接口的免费翻译实现。
/// </summary>
public sealed class GoogleFreeTranslator(HttpClient httpClient) : ITranslationProvider
{
    /// <inheritdoc />
    public string Name => "google_free";

    /// <inheritdoc />
    public bool IsAvailable => true;

    /// <inheritdoc />
    public async Task<string?> TranslateAsync(TranslationProviderRequest request, CancellationToken cancellationToken)
    {
        var encoded = Uri.EscapeDataString(request.Text);
        var url = $"https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl={request.TargetLanguage}&dt=t&q={encoded}";

        return await ProviderHttpHelper.SendWithRetryAsync(async token =>
        {
            using var response = await httpClient.GetAsync(url, token);
            if (response.StatusCode != HttpStatusCode.OK)
            {
                return (response.StatusCode, default(string));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            {
                return (response.StatusCode, default(string));
            }

            var sentenceArray = root[0];
            if (sentenceArray.ValueKind != JsonValueKind.Array)
            {
                return (response.StatusCode, default(string));
            }

            var pieces = sentenceArray
                .EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Array && item.GetArrayLength() > 0)
                .Select(item => item[0].GetString())
                .Where(value => !string.IsNullOrWhiteSpace(value));

            return (response.StatusCode, string.Concat(pieces));
        }, cancellationToken);
    }
}
