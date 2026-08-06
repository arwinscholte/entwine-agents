using System.Net.Http.Json;
using System.Text.Json;

namespace EntwineAgents.Ai;

/// <summary>
/// ENT-352: chat provider for a client's own Azure OpenAI resource. Unlike the OpenAI-compatible and
/// Anthropic providers there is no platform Azure endpoint — this provider is inherently per-client, so it
/// requires an active <c>azure-openai</c> credential (endpoint in <c>BaseUrl</c>, deployment in <c>ModelId</c>,
/// key in <c>ApiKey</c>). Azure addresses the model by deployment in the URL and authenticates with an
/// <c>api-key</c> header (not a Bearer token); the request body is otherwise OpenAI-compatible.
/// </summary>
public sealed class AzureOpenAiChatProvider : IChatProvider
{
    /// <summary>Routing key used by <see cref="ChatProviderRegistry"/> / ChatRequest.ProviderKey.</summary>
    public const string ProviderKey = "azure-openai";

    /// <summary>Azure OpenAI data-plane API version. Stable GA version; bump when adopting newer features.</summary>
    private const string ApiVersion = "2024-02-01";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICredentialStore? _credentialStore;

    public AzureOpenAiChatProvider(IHttpClientFactory httpClientFactory, ICredentialStore? credentialStore = null)
    {
        _httpClientFactory = httpClientFactory;
        _credentialStore = credentialStore;
    }

    public string Name => "Azure OpenAI";

    public async Task<string> CompleteAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (_credentialStore is null || request.ClientId is not int clientId)
            throw new InvalidOperationException(
                "azure-openai has no platform endpoint — it requires a per-client credential; ChatRequest.ClientId and a registered ICredentialStore are mandatory.");

        var cred = await _credentialStore.GetSecretAsync(clientId, ProviderKey, cancellationToken);
        if (cred is not { IsActive: true } || string.IsNullOrWhiteSpace(cred.ApiKey) || string.IsNullOrWhiteSpace(cred.BaseUrl))
            throw new InvalidOperationException($"azure-openai credential for client {clientId} is missing, inactive, or lacks a key/endpoint.");

        var deployment = request.Model ?? cred.ModelId;
        if (string.IsNullOrWhiteSpace(deployment))
            throw new InvalidOperationException($"azure-openai for client {clientId} requires a deployment name (ChatRequest.Model or the credential's ModelId).");

        var messages = new List<object>(2);
        if (!string.IsNullOrEmpty(request.SystemPrompt))
            messages.Add(new { role = "system", content = request.SystemPrompt });
        messages.Add(new { role = "user", content = request.UserPrompt });

        // Azure addresses the model via the deployment in the URL — no "model" in the body.
        var body = new Dictionary<string, object>
        {
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
        };
        if (request.JsonResponse)
            body["response_format"] = new { type = "json_object" };
        if (request.MaxTokens is int maxTokens)
            body["max_tokens"] = maxTokens;

        var uri = new Uri(new Uri(cred.BaseUrl!.TrimEnd('/') + "/"),
            $"openai/deployments/{deployment}/chat/completions?api-version={ApiVersion}");

        // Unnamed client — no baked platform headers; we set the client's absolute endpoint + api-key ourselves.
        var client = _httpClientFactory.CreateClient();
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(body) };
        httpRequest.Headers.TryAddWithoutValidation("api-key", cred.ApiKey);

        var response = await client.SendAsync(httpRequest, cancellationToken);
        response.EnsureSuccessStatusCode();

        var doc = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken);
        return OpenAiCompatibleChatProvider.ExtractContent(doc);
    }
}
