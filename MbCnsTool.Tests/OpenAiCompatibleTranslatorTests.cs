using System.Net;
using System.Text;
using System.Text.Json;
using MbCnsTool.Core.Models;
using MbCnsTool.Core.Services.Providers;

namespace MbCnsTool.Tests;

/// <summary>
/// 自定义 OpenAI 兼容翻译器测试。
/// </summary>
public sealed class OpenAiCompatibleTranslatorTests
{
    [Fact]
    public async Task TranslateAsync_Should_Send_UserAgent_And_MaxTokens()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(CreateJsonResponse("""
            {
              "choices": [
                {
                  "message": {
                    "content": "已翻译文本"
                  }
                }
              ]
            }
            """)));
        using var httpClient = new HttpClient(handler);
        var translator = new OpenAiCompatibleTranslator(
            "自定义测试引擎",
            httpClient,
            "test-key",
            "https://api.example.com/v1",
            "gpt-5-nano");

        var translated = await translator.TranslateAsync(CreateRequest("Need translation"), CancellationToken.None);

        Assert.Equal("已翻译文本", translated);
        Assert.Equal("Bearer test-key", handler.AuthorizationHeader);
        Assert.Contains("MbCnsTool/1.0", handler.UserAgentHeader);
        Assert.Equal("https://api.example.com/v1/chat/completions", handler.RequestUri?.ToString());
        Assert.NotNull(handler.RequestBody);

        using var bodyDocument = JsonDocument.Parse(handler.RequestBody!);
        var root = bodyDocument.RootElement;
        Assert.Equal("gpt-5-nano", root.GetProperty("model").GetString());
        Assert.Equal(1024, root.GetProperty("max_tokens").GetInt32());
        Assert.Equal(2, root.GetProperty("messages").GetArrayLength());
    }

    [Fact]
    public async Task TranslateAsync_Should_Parse_Array_Content()
    {
        var handler = new RecordingHttpMessageHandler((_, _) => Task.FromResult(CreateJsonResponse("""
            {
              "choices": [
                {
                  "message": {
                    "content": [
                      { "type": "output_text", "text": "第一段" },
                      { "type": "output_text", "text": "第二段" }
                    ]
                  }
                }
              ]
            }
            """)));
        using var httpClient = new HttpClient(handler);
        var translator = new OpenAiCompatibleTranslator(
            "自定义测试引擎",
            httpClient,
            "test-key",
            "https://api.example.com/v1/",
            "gpt-5-nano");

        var translated = await translator.TranslateAsync(CreateRequest("Need translation"), CancellationToken.None);

        Assert.Equal("第一段第二段", translated);
    }

    private static TranslationProviderRequest CreateRequest(string text)
    {
        return new TranslationProviderRequest
        {
            Text = text,
            Category = TextCategory.对话,
            StyleProfile = "官方直译",
            TargetLanguage = "zh-CN",
            GlossaryPrompt = "Denar=第纳尔"
        };
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responseFactory;

        public RecordingHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        public Uri? RequestUri { get; private set; }

        public string? AuthorizationHeader { get; private set; }

        public string UserAgentHeader { get; private set; } = string.Empty;

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            AuthorizationHeader = request.Headers.Authorization?.ToString();
            UserAgentHeader = request.Headers.UserAgent.ToString();
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await _responseFactory(request, cancellationToken);
        }
    }
}
