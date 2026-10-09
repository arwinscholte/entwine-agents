namespace EntwineAgents.Ai;

/// <summary>
/// The sampling fields an OpenAI chat/completions request may carry depend on the model family. The reasoning
/// families (gpt-5 and later, the o-series, the "luna" models) reject <c>max_tokens</c> ("use max_completion_tokens
/// instead") and accept only the default temperature, so a request built for gpt-4.1 fails outright against them.
/// Older models keep <c>temperature</c> and <c>max_tokens</c> exactly as before.
/// </summary>
public static class OpenAiRequestShape
{
    /// <summary>True for model families that take max_completion_tokens and only the default temperature.</summary>
    public static bool IsReasoningModel(string? model)
    {
        if (string.IsNullOrWhiteSpace(model)) return false;
        var name = model.Trim().ToLowerInvariant();
        var slash = name.LastIndexOf('/');                 // "openai/gpt-5.6-luna" on OpenRouter-style endpoints
        if (slash >= 0) name = name[(slash + 1)..];
        return name.StartsWith("gpt-5", StringComparison.Ordinal)
            || name.StartsWith("gpt-6", StringComparison.Ordinal)
            || name.StartsWith("o1", StringComparison.Ordinal)
            || name.StartsWith("o3", StringComparison.Ordinal)
            || name.StartsWith("o4", StringComparison.Ordinal)
            || name.Contains("luna", StringComparison.Ordinal);
    }

    /// <summary>
    /// Adds the temperature and token limit in the shape <paramref name="model"/> accepts: <c>temperature</c> +
    /// <c>max_tokens</c> for older models; <c>max_completion_tokens</c> and no temperature for reasoning models
    /// (their only accepted temperature is the default). A null <paramref name="maxTokens"/> adds no limit.
    /// </summary>
    public static void ApplySampling(IDictionary<string, object> body, string? model, double temperature, int? maxTokens)
    {
        if (IsReasoningModel(model))
        {
            if (maxTokens is int limit) body["max_completion_tokens"] = limit;
            return;
        }
        body["temperature"] = temperature;
        if (maxTokens is int max) body["max_tokens"] = max;
    }
}
