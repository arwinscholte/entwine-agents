using EntwineAgents.Ai;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EntwineAgentsTest.Ai;

public class ChatProviderRoutingTests
{
    private static OpenAiCompatibleChatProvider BuildOpenAi()
        => new(Mock.Of<IHttpClientFactory>(), Options.Create(new LlmOptions()));

    private static AnthropicChatProvider BuildAnthropic()
        => new(Mock.Of<IHttpClientFactory>(), Options.Create(new AnthropicOptions()));

    // ── Registry ──────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_NullKey_ReturnsDefaultOpenAi()
    {
        var openAi = BuildOpenAi();
        var registry = new ChatProviderRegistry(openAi, BuildAnthropic());

        Assert.Same(openAi, registry.Resolve(null));
        Assert.Same(openAi, registry.Resolve("   "));
    }

    [Fact]
    public void Resolve_KnownKeys_ReturnMatchingProvider()
    {
        var openAi = BuildOpenAi();
        var anthropic = BuildAnthropic();
        var registry = new ChatProviderRegistry(openAi, anthropic);

        Assert.Same(openAi, registry.Resolve(OpenAiCompatibleChatProvider.ProviderKey));
        Assert.Same(anthropic, registry.Resolve(AnthropicChatProvider.ProviderKey));
    }

    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        var anthropic = BuildAnthropic();
        var registry = new ChatProviderRegistry(BuildOpenAi(), anthropic);

        Assert.Same(anthropic, registry.Resolve("ANTHROPIC"));
    }

    [Fact]
    public void Resolve_UnknownKey_FallsBackToDefault()
    {
        var openAi = BuildOpenAi();
        var registry = new ChatProviderRegistry(openAi, BuildAnthropic());

        Assert.Same(openAi, registry.Resolve("does-not-exist"));
    }

    // ── Router ────────────────────────────────────────────────────────────

    private sealed class RecordingProvider : IChatProvider
    {
        public ChatRequest? LastRequest;
        private readonly string _result;
        public RecordingProvider(string result) => _result = result;
        public string Name => "recording";
        public Task<string> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(_result);
        }
    }

    [Fact]
    public async Task Router_DelegatesToResolvedProvider_PassingKeyAndRequest()
    {
        var inner = new RecordingProvider("delegated-result");
        var registry = new Mock<IChatProviderRegistry>();
        registry.Setup(r => r.Resolve(It.IsAny<string?>())).Returns(inner);
        var router = new RoutingChatProvider(registry.Object);

        var request = new ChatRequest("u", ProviderKey: "anthropic");
        var result = await router.CompleteAsync(request);

        Assert.Equal("delegated-result", result);
        registry.Verify(r => r.Resolve("anthropic"), Times.Once);
        Assert.Same(request, inner.LastRequest);
    }
}
