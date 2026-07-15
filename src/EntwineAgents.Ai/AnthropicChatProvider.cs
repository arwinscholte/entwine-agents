using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EntwineAgents.Ai;

/// <summary>
/// Chat provider for the Anthropic-native Messages API (/v1/messages).
/// Differs from the OpenAI-compatible shape: x-api-key + anthropic-version headers
/// (set on the named "Anthropic" client), a top-level <c>system</c> field rather than a
/// system message, a required <c>max_tokens</c>, and a <c>content</c> array of typed
/// blocks in the response.
///
/// Anthropic has no <c>response_format = json_object</c>, so <see cref="ChatRequest.JsonResponse"/>
/// is best-effort here and relies on the prompt instructing JSON output (which every
/// current call site already does).
/// </summary>
public sealed class AnthropicChatProvider : IChatProvider
{
    /// <summary>Routing key used by <see cref="ChatProviderRegistry"/> / ChatRequest.ProviderKey.</summary>
    public const string ProviderKey = "anthropic";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AnthropicOptions _options;

    public AnthropicChatProvider(IHttpClientFactory httpClientFactory, IOptions<AnthropicOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public string Name => "Anthropic";

    public async Task<string> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("Anthropic");

        var body = new Dictionary<string, object>
        {
            ["model"] = request.Model ?? _options.ModelId,
            ["max_tokens"] = request.MaxTokens ?? _options.DefaultMaxTokens,
            ["messages"] = new[] { new { role = "user", content = request.UserPrompt } },
            ["temperature"] = request.Temperature,
        };
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            body["system"] = request.SystemPrompt;

        var response = await client.PostAsJsonAsync("v1/messages", body, cancellationToken);
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return ExtractContent(doc);
    }

    /// <summary>Anthropic returns content as an array of typed blocks; concatenate the text ones.</summary>
    private static string ExtractContent(JsonElement doc)
    {
        if (!doc.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var sb = new StringBuilder();
        foreach (var block in content.EnumerateArray())
            if (block.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                sb.Append(t.GetString());
        return sb.ToString();
    }
}
