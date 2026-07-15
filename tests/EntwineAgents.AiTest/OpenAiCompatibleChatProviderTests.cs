using System.Net;
using System.Text.Json;
using EntwineAgents.Ai;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EntwineAgentsTest.Ai;

public class OpenAiCompatibleChatProviderTests
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

    private static OpenAiCompatibleChatProvider Build(RecordingHandler handler, LlmOptions? opts = null)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("LLM"))
            .Returns(() => new HttpClient(handler) { BaseAddress = new Uri("https://localhost/v1/") });
        return new OpenAiCompatibleChatProvider(factory.Object, Options.Create(opts ?? new LlmOptions { ModelId = "default-model" }));
    }

    private static JsonElement Body(RecordingHandler h) => JsonDocument.Parse(h.RequestBody!).RootElement;

    [Fact]
    public async Task CompleteAsync_SystemAndUser_JsonAndTemperature_SentAndParsed()
    {
        var handler = new RecordingHandler("{\"choices\":[{\"message\":{\"content\":\"hello world\"}}]}");
        var provider = Build(handler);

        var result = await provider.CompleteAsync(new ChatRequest("the user", "the system", Temperature: 0.2, JsonResponse: true));

        Assert.Equal("hello world", result);
        Assert.EndsWith("chat/completions", handler.RequestUri);
        var body = Body(handler);
        Assert.Equal("default-model", body.GetProperty("model").GetString());
        Assert.Equal("json_object", body.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(0.2, body.GetProperty("temperature").GetDouble(), 3);
        var messages = body.GetProperty("messages");
        Assert.Equal(2, messages.GetArrayLength());
        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
        Assert.Equal("the user", messages[1].GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_NoSystem_NoJson_OmitsThem()
    {
        var handler = new RecordingHandler("{\"choices\":[{\"message\":{\"content\":\"x\"}}]}");
        var provider = Build(handler);

        await provider.CompleteAsync(new ChatRequest("just user", SystemPrompt: null, JsonResponse: false));

        var body = Body(handler);
        Assert.Single(body.GetProperty("messages").EnumerateArray());
        Assert.Equal("user", body.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.False(body.TryGetProperty("response_format", out _));
    }

    [Fact]
    public async Task CompleteAsync_ModelOverride_UsesIt()
    {
        var handler = new RecordingHandler("{\"choices\":[{\"message\":{\"content\":\"x\"}}]}");
        var provider = Build(handler);

        await provider.CompleteAsync(new ChatRequest("u", Model: "override-model"));

        Assert.Equal("override-model", Body(handler).GetProperty("model").GetString());
    }

    [Fact]
    public async Task CompleteAsync_ContentAsArray_Concatenated()
    {
        var handler = new RecordingHandler("{\"choices\":[{\"message\":{\"content\":[{\"type\":\"text\",\"text\":\"a\"},{\"type\":\"text\",\"text\":\"b\"}]}}]}");
        var result = await Build(handler).CompleteAsync(new ChatRequest("u"));
        Assert.Equal("ab", result);
    }

    [Fact]
    public async Task CompleteAsync_ContentNull_ReturnsEmpty()
    {
        var handler = new RecordingHandler("{\"choices\":[{\"message\":{\"content\":null}}]}");
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
