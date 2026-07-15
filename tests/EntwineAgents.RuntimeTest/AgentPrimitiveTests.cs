using EntwineAgents.Runtime;
using FluentAssertions;

namespace EntwineAgents.RuntimeTest;

public class JsonTextTests
{
    [Fact]
    public void Plain_json_is_returned_trimmed_unchanged()
        => JsonText.Unfence("  {\"a\":1}  ").Should().Be("{\"a\":1}");

    [Fact]
    public void Strips_a_json_fenced_block()
        => JsonText.Unfence("```json\n{\"a\":1}\n```").Should().Be("{\"a\":1}");

    [Fact]
    public void Strips_a_bare_fence()
        => JsonText.Unfence("```\n{\"a\":1}\n```").Should().Be("{\"a\":1}");

    [Fact]
    public void Empty_stays_empty() => JsonText.Unfence("   ").Should().Be("   ");
}

public class AgentPrimitiveTests
{
    private sealed class FakeChat(string response, bool throws = false) : IAgentChat
    {
        public ChatTurn? Last { get; private set; }
        public Task<string> CompleteAsync(ChatTurn turn, CancellationToken ct = default)
        {
            Last = turn;
            return throws ? throw new InvalidOperationException("boom") : Task.FromResult(response);
        }
    }

    private sealed class StubPrompts(string text) : IPromptSource
    {
        public string? RequestedKey { get; private set; }
        public Task<string> GetAsync(string key, string fallback, CancellationToken ct = default)
        {
            RequestedKey = key;
            return Task.FromResult(text);
        }
    }

    [Fact]
    public async Task Parses_the_raw_result_on_success()
    {
        var result = await AgentPrimitive.RunAsync(
            new FakeChat("42"), new DefaultPromptSource(),
            "k", "fallback-system", "user text",
            parse: int.Parse, onFailure: -1);

        result.Should().Be(42);
    }

    [Fact]
    public async Task Degrades_to_onFailure_when_the_chat_throws()
    {
        var result = await AgentPrimitive.RunAsync(
            new FakeChat("unused", throws: true), new DefaultPromptSource(),
            "k", "fallback-system", "user text",
            parse: int.Parse, onFailure: -1);

        result.Should().Be(-1);   // parse never runs; the caller's typed "couldn't run" value comes back
    }

    [Fact]
    public async Task Sends_the_prompt_from_the_source_by_key_with_the_built_turn()
    {
        var chat = new FakeChat("ok");
        var prompts = new StubPrompts("OVERRIDDEN SYSTEM");

        await AgentPrimitive.RunAsync(chat, prompts, "some.key", "builtin-fallback", "the user prompt",
            parse: s => s, onFailure: "", json: true, temperature: 0.2);

        prompts.RequestedKey.Should().Be("some.key");
        chat.Last!.System.Should().Be("OVERRIDDEN SYSTEM");   // source wins over the fallback
        chat.Last.User.Should().Be("the user prompt");
        chat.Last.Json.Should().BeTrue();
    }
}
