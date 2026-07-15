# EntwineAgents.Prompts

Prompt management for .NET agents: **prompts are data**, not compiled constants.

- `IPromptService` / `PromptService` — versioned templates with a per-client override and a cached
  client-to-global fallback read path.
- `IPromptRepository` — persistence is a port; bring any store (an EF adapter is a few dozen lines).
- `IPromptSavedHook` — react when a version is superseded (A/B analysis, audit, cache bust) without
  coupling the service to your infrastructure.
- `ClientScopedPromptSource` — binds an agent's `IPromptSource` to the service, scoped to a tenant.
- `HttpPromptSource` — for hosts with **no database**: resolve prompts over a remote prompt-egress endpoint,
  cached, and failing OPEN to the built-in fallback — a prompt-service outage can never take an agent down.

Part of [EntwineAgents](https://github.com/arwinscholte/entwine-agents) — a lean, composable agent runtime
for .NET. Pairs with `EntwineAgents.Runtime`, whose agents load prompts by key.
