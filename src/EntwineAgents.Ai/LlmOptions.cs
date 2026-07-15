namespace EntwineAgents.Ai;

// ENT-321: these two option records live in the lean EntwineAgents.Ai assembly (the chat providers bind to
// them) in the `EntwineAgents.Ai` namespace (moved from EntwineAgents.Models as pre-split polish so the public package owns its options; every
// call site adds `using EntwineAgents.Ai;`).

/// <summary>
/// Configuration options for the LLM provider.
/// Supports any OpenAI-compatible endpoint (OpenRouter, Groq, Together AI, Ollama, etc.)
/// by changing BaseUrl and ApiKey. Vision calls require an OpenAI-compatible vision endpoint.
/// </summary>
public class LlmOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1/";
    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = "gpt-4.1-nano";
    /// <summary>
    /// Vision-capable model for image-based document extraction.
    /// Set to null or empty to skip the vision pass entirely.
    /// </summary>
    public string? VisionModelId { get; set; } = "gpt-4o";
    /// <summary>HTTP header name for authentication. Default: Authorization</summary>
    public string ApiKeyHeader { get; set; } = "Authorization";
    /// <summary>Prefix prepended to ApiKey in the header. Default: "Bearer " (with trailing space)</summary>
    public string ApiKeyPrefix { get; set; } = "Bearer ";
}

/// <summary>
/// Configuration for the Anthropic-native Messages API (/v1/messages), used by
/// AnthropicChatProvider. Distinct from the OpenAI-compatible LLM client: x-api-key
/// auth + anthropic-version header, and a required max_tokens.
/// </summary>
public class AnthropicOptions
{
    public string BaseUrl { get; set; } = "https://api.anthropic.com/";
    public string ApiKey { get; set; } = string.Empty;
    public string ModelId { get; set; } = "claude-opus-4-8";
    /// <summary>Anthropic API version header value (anthropic-version).</summary>
    public string AnthropicVersion { get; set; } = "2023-06-01";
    /// <summary>Anthropic requires max_tokens; used when a ChatRequest doesn't specify one.</summary>
    public int DefaultMaxTokens { get; set; } = 1024;
}
