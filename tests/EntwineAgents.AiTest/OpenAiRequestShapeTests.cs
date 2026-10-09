using System.Net;
using System.Text.Json;
using EntwineAgents.Ai;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace EntwineAgentsTest.Ai;

/// <summary>
/// gpt-5 / o-series / luna models reject max_tokens and any non-default temperature (checked against the live API,
/// 2026-10-09: "'max_tokens' is not supported with this model. Use 'max_completion_tokens' instead." and "Only the
/// default (1) value is supported."). Older models keep the request shape they had.
/// </summary>
public class OpenAiRequestShapeTests
{
    [Theory]
    [InlineData("gpt-5.6-luna", true)]
    [InlineData("gpt-5-nano", true)]
    [InlineData("gpt-5.4-nano", true)]
    [InlineData("gpt-6-luna", true)]
    [InlineData("o3-mini", true)]
    [InlineData("o4-mini", true)]
    [InlineData("openai/gpt-5.6-luna", true)]
    [InlineData("GPT-5.6-LUNA", true)]
    [InlineData("gpt-4.1-nano", false)]
    [InlineData("gpt-4o-mini", false)]
    [InlineData("llama-3.1-70b", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Reasoning_families_are_recognised_by_name(string? model, bool expected)
        => Assert.Equal(expected, OpenAiRequestShape.IsReasoningModel(model));

    [Fact]
    public void Reasoning_models_get_max_completion_tokens_and_no_temperature()
    {
        var body = new Dictionary<string, object>();
        OpenAiRequestShape.ApplySampling(body, "gpt-5.6-luna", temperature: 0.2, maxTokens: 800);
        Assert.Equal(800, body["max_completion_tokens"]);
        Assert.False(body.ContainsKey("max_tokens"));
        Assert.False(body.ContainsKey("temperature"));
    }

    [Fact]
    public void Older_models_keep_temperature_and_max_tokens()
    {
        var body = new Dictionary<string, object>();
        OpenAiRequestShape.ApplySampling(body, "gpt-4.1-nano", temperature: 0.2, maxTokens: 800);
        Assert.Equal(0.2, body["temperature"]);
        Assert.Equal(800, body["max_tokens"]);
        Assert.False(body.ContainsKey("max_completion_tokens"));
    }

    [Fact]
    public void No_limit_adds_no_token_field()
    {
        var body = new Dictionary<string, object>();
        OpenAiRequestShape.ApplySampling(body, "gpt-5.6-luna", temperature: 0.0, maxTokens: null);
        Assert.Empty(body);
    }

    [Theory]
    [InlineData("gpt-5.6-luna", "none", "none")]
    [InlineData("gpt-5.6-luna", " Low ", "low")]
    [InlineData("gpt-5.6-luna", null, null)]
    [InlineData("gpt-5.6-luna", "", null)]
    [InlineData("gpt-4.1-nano", "none", null)]   // older models never get it
    public void Reasoning_effort_is_sent_only_to_reasoning_models_and_only_when_set(string model, string? effort, string? expected)
    {
        var body = new Dictionary<string, object>();
        OpenAiRequestShape.ApplySampling(body, model, 0.0, 100, effort);
        if (expected is null) Assert.False(body.ContainsKey("reasoning_effort"));
        else Assert.Equal(expected, body["reasoning_effort"]);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? RequestBody;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"choices\":[{\"message\":{\"content\":\"{}\"}}]}") };
        }
    }

    private static (OpenAiCompatibleChatProvider Provider, RecordingHandler Handler) Luna(string? optionEffort)
    {
        var handler = new RecordingHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("LLM")).Returns(() => new HttpClient(handler) { BaseAddress = new Uri("https://localhost/v1/") });
        return (new OpenAiCompatibleChatProvider(factory.Object, Options.Create(new LlmOptions { ModelId = "gpt-5.6-luna", ReasoningEffort = optionEffort })), handler);
    }

    [Fact]
    public async Task The_option_sets_reasoning_effort_and_a_request_overrides_it()
    {
        var (provider, handler) = Luna("none");
        await provider.CompleteAsync(new ChatRequest("u"));
        Assert.Equal("none", JsonDocument.Parse(handler.RequestBody!).RootElement.GetProperty("reasoning_effort").GetString());

        await provider.CompleteAsync(new ChatRequest("u", ReasoningEffort: "high"));
        Assert.Equal("high", JsonDocument.Parse(handler.RequestBody!).RootElement.GetProperty("reasoning_effort").GetString());

        var (plain, plainHandler) = Luna(null);
        await plain.CompleteAsync(new ChatRequest("u"));
        Assert.False(JsonDocument.Parse(plainHandler.RequestBody!).RootElement.TryGetProperty("reasoning_effort", out _), "no option, no request value: the model's default");
    }

    [Fact]
    public async Task The_provider_sends_the_reasoning_shape_for_a_luna_model()
    {
        var handler = new RecordingHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("LLM")).Returns(() => new HttpClient(handler) { BaseAddress = new Uri("https://localhost/v1/") });
        var provider = new OpenAiCompatibleChatProvider(factory.Object, Options.Create(new LlmOptions { ModelId = "gpt-5.6-luna" }));

        await provider.CompleteAsync(new ChatRequest("u", Temperature: 0.2, JsonResponse: true, MaxTokens: 500));

        var body = JsonDocument.Parse(handler.RequestBody!).RootElement;
        Assert.Equal(500, body.GetProperty("max_completion_tokens").GetInt32());
        Assert.False(body.TryGetProperty("max_tokens", out _));
        Assert.False(body.TryGetProperty("temperature", out _));
        Assert.Equal("json_object", body.GetProperty("response_format").GetProperty("type").GetString());
    }
}
