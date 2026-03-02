using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MbCnsTool.Core.Abstractions;
using MbCnsTool.Core.Models;

namespace MbCnsTool.Core.Services.Providers;

/// <summary>
/// OpenAI 兼容协议翻译器，可对接 ChatGPT/DeepSeek/通义千问兼容网关。
/// </summary>
public sealed class OpenAiCompatibleTranslator : ITranslationProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _baseUrl;
    private readonly string _model;

    /// <summary>
    /// 初始化兼容翻译器。
    /// </summary>
    public OpenAiCompatibleTranslator(string name, HttpClient httpClient, string apiKey, string baseUrl, string model)
    {
        Name = name;
        _httpClient = httpClient;
        _apiKey = apiKey;
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public bool IsAvailable => !string.IsNullOrWhiteSpace(_apiKey);

    /// <inheritdoc />
    public async Task<string?> TranslateAsync(TranslationProviderRequest request, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return null;
        }

        var endpoint = $"{_baseUrl}/chat/completions";
        var systemPrompt = $"你是骑砍2 Mod 汉化引擎。要求：只输出译文，不解释。风格：{request.StyleProfile}。文本类型：{request.Category}。目标语言：{request.TargetLanguage}。";
        var glossaryPrompt = string.IsNullOrWhiteSpace(request.GlossaryPrompt)
            ? "无术语表。"
            : $"术语表：{request.GlossaryPrompt}";
        var userPrompt = $"{glossaryPrompt}\n请翻译以下文本，保留占位符:\n{request.Text}";

        return await ProviderHttpHelper.SendWithRetryAsync(async token =>
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            message.Content = JsonContent.Create(new
            {
                model = _model,
                temperature = 0.1,
                messages = new object[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userPrompt }
                }
            });

            using var response = await _httpClient.SendAsync(message, token);
            if ((int)response.StatusCode < 200 || (int)response.StatusCode >= 300)
            {
                return (response.StatusCode, default(string));
            }

            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var root = document.RootElement;
            if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
            {
                return (HttpStatusCode.InternalServerError, default(string));
            }

            var content = choices[0].GetProperty("message").GetProperty("content").GetString();
            return (response.StatusCode, content?.Trim());
        }, cancellationToken);
    }
}
