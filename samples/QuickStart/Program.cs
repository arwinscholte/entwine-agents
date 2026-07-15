// QuickStart: the whole idea in one file.
//
// An agent = a prompt (by key, host-overridable) + input shaping + typed parsing + graceful degrade,
// riding a shell that owns the loop. Run with any OpenAI-compatible key:
//
//   set OPENAI_API_KEY=sk-...        (or export on unix; optionally OPENAI_BASE_URL / OPENAI_MODEL)
//   dotnet run --project samples/QuickStart

using EntwineAgents.Ai;
using EntwineAgents.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
if (string.IsNullOrWhiteSpace(apiKey))
{
    Console.WriteLine("Set OPENAI_API_KEY (any OpenAI-compatible endpoint works — set OPENAI_BASE_URL to point elsewhere).");
    return;
}

var baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1/";
var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4.1-nano";

// Wire the provider seam (IChatProvider) — hosts normally do this once in DI.
var services = new ServiceCollection();
services.AddHttpClient("LLM", c =>
{
    c.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
    c.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
});
await using var sp = services.BuildServiceProvider();
var provider = new OpenAiCompatibleChatProvider(
    sp.GetRequiredService<IHttpClientFactory>(),
    Options.Create(new LlmOptions { ModelId = model }));

// Bridge the provider seam to the agent seam and run the agent.
var agent = new TaglineAgent(new ChatProviderAgentChat(provider));
var product = args.Length > 0 ? string.Join(' ', args) : "a keyboard for cats";
Console.WriteLine(await agent.RunAsync(product));

/// <summary>A minimal agent: four overrides carry everything that is yours; the shell owns the loop.</summary>
sealed class TaglineAgent(IAgentChat chat, IPromptSource? prompts = null) : Agent<string, string>(chat, prompts)
{
    protected override string Key => "quickstart.tagline.system";   // hosts can override by key
    protected override string FallbackPrompt =>
        "You write one short, punchy product tagline. Reply with the tagline only — no quotes, no preamble.";
    protected override bool Json => false;
    protected override string BuildUser(string product) => $"Product: {product}";
    protected override string Parse(string raw) => raw.Trim();
    protected override string OnFailure => "(the model was unavailable — try again)";
}
