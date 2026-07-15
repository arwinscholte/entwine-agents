using System.Net;
using System.Text.Json;
using EntwineAgents.Ai;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EntwineAgentsTest.Ai;

public class AnthropicChatProviderTests
{
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? RequestBody;
        public string? RequestUri;
        private readonly string _responseJson;
        private readonly HttpStatusCode _status;
        public RecordingHandler(string responseJson, HttpStatusCode status = HttpStatusCode.OK)
        { _responseJson = responseJson; _status = status; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri?.ToString();
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(_status) { Content = new StringContent(_responseJson) };
        }
    }

    private static AnthropicChatProvider Build(RecordingHandler handler, AnthropicOptions? opts = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("Anthropic"))
            .Returns(() => new HttpClient(handler) { BaseAddress = new Uri("https://api.anthropic.com/") });
        return new AnthropicChatProvider(factory.Object,
            Options.Create(opts ?? new AnthropicOptions { ModelId = "claude-default", DefaultMaxTokens = 777 }));
    }

    private static JsonElement Body(RecordingHandler h) => JsonDocument.Parse(h.RequestBody!).RootElement;

    [Fact]
    public async Task CompleteAsync_SystemAndUser_DefaultMaxTokens_SentAndParsed()
    {
        var handler = new RecordingHandler("{\"content\":[{\"type\":\"text\",\"text\":\"hello world\"}]}");
        var provider = Build(handler);

        var result = await provider.CompleteAsync(new ChatRequest("the user", "the system", Temperature: 0.2));

        Assert.Equal("hello world", result);
        Assert.EndsWith("v1/messages", handler.RequestUri);
        var body = Body(handler);
        Assert.Equal("claude-default", body.GetProperty("model").GetString());
        Assert.Equal(777, body.GetProperty("max_tokens").GetInt32());
        Assert.Equal("the system", body.GetProperty("system").GetString());
        Assert.Equal(0.2, body.GetProperty("temperature").GetDouble(), 3);
        var messages = body.GetProperty("messages");
        Assert.Single(messages.EnumerateArray());
        Assert.Equal("user", messages[0].GetProperty("role").GetString());
        Assert.Equal("the user", messages[0].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_NoSystem_OmitsSystemField()
    {
        var handler = new RecordingHandler("{\"content\":[{\"type\":\"text\",\"text\":\"x\"}]}");
        var provider = Build(handler);

        await provider.CompleteAsync(new ChatRequest("just user", SystemPrompt: null));

        Assert.False(Body(handler).TryGetProperty("system", out _));
    }

    [Fact]
    public async Task CompleteAsync_ModelAndMaxTokensOverride_UsesThem()
    {
        var handler = new RecordingHandler("{\"content\":[{\"type\":\"text\",\"text\":\"x\"}]}");
        var provider = Build(handler);

        await provider.CompleteAsync(new ChatRequest("u", Model: "claude-override", MaxTokens: 200));

        var body = Body(handler);
        Assert.Equal("claude-override", body.GetProperty("model").GetString());
        Assert.Equal(200, body.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task CompleteAsync_MultipleTextBlocks_Concatenated()
    {
        var handler = new RecordingHandler("{\"content\":[{\"type\":\"text\",\"text\":\"a\"},{\"type\":\"text\",\"text\":\"b\"}]}");
        var result = await Build(handler).CompleteAsync(new ChatRequest("u"));
        Assert.Equal("ab", result);
    }

    [Fact]
    public async Task CompleteAsync_NoContentArray_ReturnsEmpty()
    {
        var handler = new RecordingHandler("{\"role\":\"assistant\"}");
        var result = await Build(handler).CompleteAsync(new ChatRequest("u"));
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task CompleteAsync_NonSuccess_Throws()
    {
        var handler = new RecordingHandler("error", HttpStatusCode.InternalServerError);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() => Build(handler).CompleteAsync(new ChatRequest("u")));
    }
}
