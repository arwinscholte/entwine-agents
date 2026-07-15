# Contributing

Thanks for your interest. A few ground rules keep this codebase what it is:

## Principles (enforced in review)

1. **No domain, no persistence in the core.** These packages are mechanism. If your change needs a database,
   it belongs behind a port (see `IPromptRepository`, `IDocumentOcr`); if it encodes business rules, it
   belongs in your application, not here.
2. **Fail open, degrade typed.** New code paths that talk to a model or the network must have a typed
   degrade — never let a transport error escape as an exception through an agent.
3. **Agents propose; algorithms compute.** Don't add code that lets a model's raw output flow anywhere
   unparsed and unvalidated.
4. **Tests come with the change.** Every behavioural change ships with tests in the matching `tests/` project.
   Fakes over mocks where practical — see the existing scripted `IAgentChat`/`IChatProvider` fakes.

## Practicalities

- .NET 10 SDK; `dotnet build entwine-agents.slnx && dotnet test entwine-agents.slnx` must be green.
- Match the surrounding style (file-scoped namespaces, expression bodies where they clarify, doc comments
  that say *why*).
- Small, focused PRs with a clear description of the behaviour change.

## Reporting issues

Include the package, the smallest reproduction you can manage, and — for agent-behaviour issues — the raw
model output if you have it (redact anything sensitive; that's what `PseudonymMap` is for).
