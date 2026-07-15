using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace EntwineAgents.Ai;

/// <summary>
/// Chat provider for any OpenAI-compatible endpoint (OpenAI, OpenRouter, Groq, Ollama, …).
/// Wraps the existing named "LLM" HttpClient + LlmOptions — behaviour-identical to the inline
/// chat/completions calls it replaces: same endpoint, body shape, success check and parse.
/// </summary>
public sealed class OpenAiCompatibleChatProvider : IChatProvider
{
    /// <summary>Routing key used by <see cref="ChatProviderRegistry"/> / ChatRequest.ProviderKey.</summary>
    public const string ProviderKey = "openai";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly LlmOptions _options;

    public OpenAiCompatibleChatProvider(IHttpClientFactory httpClientFactory, IOptions<LlmOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public string Name => "OpenAI-compatible";

    public async Task<string> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient("LLM");

        var messages = new List<object>(2);
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            messages.Add(new { role = "system", content = request.SystemPrompt });
        messages.Add(new { role = "user", content = request.UserPrompt });

        var body = new Dictionary<string, object>
        {
            ["model"] = request.Model ?? _options.ModelId,
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
        };
        if (request.JsonResponse)
            body["response_format"] = new { type = "json_object" };
        if (request.MaxTokens is int maxTokens)
            body["max_tokens"] = maxTokens;

        var response = await client.PostAsJsonAsync("chat/completions", body, cancellationToken);
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return ExtractContent(doc);
    }

    /// <summary>content is usually a string; tolerate array-of-parts / null shapes too.</summary>
    private static string ExtractContent(JsonElement doc)
    {
        if (!doc.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return string.Empty;
        var message = choices[0].GetProperty("message");
        if (!message.TryGetProperty("content", out var content))
            return string.Empty;

        if (content.ValueKind == JsonValueKind.String)
            return content.GetString() ?? string.Empty;

        if (content.ValueKind == JsonValueKind.Array)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var part in content.EnumerateArray())
                if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                    sb.Append(t.GetString());
            return sb.ToString();
        }

        return string.Empty;
    }
}
