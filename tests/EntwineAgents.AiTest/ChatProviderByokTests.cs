using System.Net;
using System.Text.Json;
using EntwineAgents.Ai;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EntwineAgentsTest.Ai;

/// <summary>
/// ENT-352: providers resolve a per-client BYOK credential from <see cref="ICredentialStore"/> and route the
/// outbound call to the client's key/endpoint/model; with no store, no ClientId, or no active credential they
/// fall back to the platform key (unchanged behaviour).
/// </summary>
public class ChatProviderByokTests
{
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Uri;
        public string? Authorization;
        public string? ApiKey;      // x-api-key (Anthropic) or api-key (Azure)
        public string? Body;
        private readonly string _response;
        public CapturingHandler(string response) => _response = response;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Uri = request.RequestUri?.ToString();
            if (request.Headers.TryGetValues("Authorization", out var a)) Authorization = string.Join(",", a);
            if (request.Headers.TryGetValues("x-api-key", out var x)) ApiKey = string.Join(",", x);
            if (request.Headers.TryGetValues("api-key", out var k)) ApiKey = string.Join(",", k);
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_response) };
        }
    }

    private const string OpenAiOk = "{\"choices\":[{\"message\":{\"content\":\"ok\"}}]}";
    private const string AnthropicOk = "{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}";

    private static IHttpClientFactory Factory(CapturingHandler handler, string baseAddress)
    {
        var f = new Mock<IHttpClientFactory>();
        f.Setup(x => x.CreateClient(It.IsAny<string>()))
            .Returns(() => new HttpClient(handler) { BaseAddress = new Uri(baseAddress) });
        return f.Object;
    }

    private static ICredentialStore Store(string providerKey, ProviderCredentialDto? cred)
    {
        var s = new Mock<ICredentialStore>();
        s.Setup(x => x.GetSecretAsync(It.IsAny<int>(), providerKey, It.IsAny<CancellationToken>())).ReturnsAsync(cred);
        return s.Object;
    }

    private static ProviderCredentialDto Cred(string key, string? baseUrl, string? model, bool active = true)
        => new(ProviderKey: "x", ApiKey: key, BaseUrl: baseUrl, ModelId: model, IsActive: active, UpdatedAt: DateTime.UtcNow);

    private static JsonElement Body(CapturingHandler h) => JsonDocument.Parse(h.Body!).RootElement;

    // ── OpenAI-compatible ──────────────────────────────────────────────────

    [Fact]
    public async Task OpenAi_WithClientCredential_UsesClientKeyEndpointModel()
    {
        var handler = new CapturingHandler(OpenAiOk);
        var provider = new OpenAiCompatibleChatProvider(
            Factory(handler, "https://platform.example/v1/"),
            Options.Create(new LlmOptions { ModelId = "platform-model" }),
            Store(OpenAiCompatibleChatProvider.ProviderKey, Cred("sk-client", "https://client.example/v1", "client-model")));

        await provider.CompleteAsync(new ChatRequest("u", ClientId: 7));

        Assert.Equal("https://client.example/v1/chat/completions", handler.Uri);
        Assert.Equal("Bearer sk-client", handler.Authorization);
        Assert.Equal("client-model", Body(handler).GetProperty("model").GetString());
    }

    [Fact]
    public async Task OpenAi_NoActiveCredential_FallsBackToPlatform()
    {
        var handler = new CapturingHandler(OpenAiOk);
        var provider = new OpenAiCompatibleChatProvider(
            Factory(handler, "https://platform.example/v1/"),
            Options.Create(new LlmOptions { ModelId = "platform-model" }),
            Store(OpenAiCompatibleChatProvider.ProviderKey, Cred("sk-client", "https://client.example/v1", "client-model", active: false)));

        await provider.CompleteAsync(new ChatRequest("u", ClientId: 7));

        Assert.Equal("https://platform.example/v1/chat/completions", handler.Uri); // platform endpoint
        Assert.Null(handler.Authorization);                                         // no BYOK override
        Assert.Equal("platform-model", Body(handler).GetProperty("model").GetString());
    }

    [Fact]
    public async Task OpenAi_NoStore_FallsBackToPlatform()
    {
        var handler = new CapturingHandler(OpenAiOk);
        var provider = new OpenAiCompatibleChatProvider(
            Factory(handler, "https://platform.example/v1/"),
            Options.Create(new LlmOptions { ModelId = "platform-model" })); // no credential store

        await provider.CompleteAsync(new ChatRequest("u", ClientId: 7));

        Assert.Equal("https://platform.example/v1/chat/completions", handler.Uri);
        Assert.Equal("platform-model", Body(handler).GetProperty("model").GetString());
    }

    [Fact]
    public async Task OpenAi_ClientKeyOnly_NoBaseUrl_UsesPlatformEndpointWithClientKey()
    {
        var handler = new CapturingHandler(OpenAiOk);
        var provider = new OpenAiCompatibleChatProvider(
            Factory(handler, "https://platform.example/v1/"),
            Options.Create(new LlmOptions { ModelId = "platform-model" }),
            Store(OpenAiCompatibleChatProvider.ProviderKey, Cred("sk-client", baseUrl: null, model: null)));

        await provider.CompleteAsync(new ChatRequest("u", ClientId: 7));

        Assert.Equal("https://platform.example/v1/chat/completions", handler.Uri); // same endpoint…
        Assert.Equal("Bearer sk-client", handler.Authorization);                    // …but the client's key
        Assert.Equal("platform-model", Body(handler).GetProperty("model").GetString());
    }

    // ── Azure OpenAI (per-client only) ─────────────────────────────────────

    [Fact]
    public async Task AzureOpenAi_BuildsDeploymentUrl_AndApiKeyHeader()
    {
        var handler = new CapturingHandler(OpenAiOk);
        var provider = new AzureOpenAiChatProvider(
            Factory(handler, "https://unused/"),
            Store(AzureOpenAiChatProvider.ProviderKey, Cred("azkey", "https://acme.openai.azure.com", "gpt4o-deploy")));

        await provider.CompleteAsync(new ChatRequest("u", ClientId: 7));

        Assert.Equal("https://acme.openai.azure.com/openai/deployments/gpt4o-deploy/chat/completions?api-version=2024-02-01", handler.Uri);
        Assert.Equal("azkey", handler.ApiKey);
        Assert.False(Body(handler).TryGetProperty("model", out _)); // deployment is in the URL, not the body
    }

    [Fact]
    public async Task AzureOpenAi_NoCredential_Throws()
    {
        var handler = new CapturingHandler(OpenAiOk);
        var provider = new AzureOpenAiChatProvider(Factory(handler, "https://unused/")); // no store

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.CompleteAsync(new ChatRequest("u", ClientId: 7)));
    }

    // ── Anthropic ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Anthropic_WithClientCredential_OverridesApiKey()
    {
        var handler = new CapturingHandler(AnthropicOk);
        var provider = new AnthropicChatProvider(
            Factory(handler, "https://api.anthropic.com/"),
            Options.Create(new AnthropicOptions { ModelId = "platform-claude" }),
            Store(AnthropicChatProvider.ProviderKey, Cred("ant-client", baseUrl: null, model: "client-claude")));

        await provider.CompleteAsync(new ChatRequest("u", ClientId: 7));

        Assert.Equal("ant-client", handler.ApiKey);                          // x-api-key overridden
        Assert.EndsWith("v1/messages", handler.Uri);
        Assert.Equal("client-claude", Body(handler).GetProperty("model").GetString());
    }
}
