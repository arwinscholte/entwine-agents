# EntwineAgents.Ai

The provider seam for .NET LLM agents: one `IChatProvider` interface, ready-made **OpenAI-compatible**
(OpenAI, OpenRouter, Groq, Ollama, ...) and **Anthropic-native** providers, and a registry that routes per
request via `ChatRequest.ProviderKey`.

```csharp
var provider = new OpenAiCompatibleChatProvider(httpClientFactory, Options.Create(new LlmOptions
{
    ModelId = "gpt-4.1-nano",
}));

var answer = await provider.CompleteAsync(new ChatRequest(
    UserPrompt: "Say hi in five words.",
    SystemPrompt: "You are terse.",
    JsonResponse: false));
```

Your agents never learn which vendor is behind them — swap providers in DI, or route per call.

Part of [EntwineAgents](https://github.com/arwinscholte/entwine-agents) — a lean, composable agent runtime
for .NET. Pairs with `EntwineAgents.Runtime` for typed agent shells.
