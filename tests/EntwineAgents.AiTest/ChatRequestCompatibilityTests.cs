using EntwineAgents.Ai;
using Xunit;

namespace EntwineAgentsTest.Ai;

/// <summary>
/// Packages built against an earlier EntwineAgents.Ai call ChatRequest's constructor by its exact signature. Adding a
/// parameter replaces that signature, so every earlier shape stays as an overload.
/// </summary>
public class ChatRequestCompatibilityTests
{
    [Fact]
    public void The_0_3_4_constructor_still_exists()
    {
        var ctor = typeof(ChatRequest).GetConstructor(
        [
            typeof(string), typeof(string), typeof(string), typeof(double), typeof(bool),
            typeof(int?), typeof(string), typeof(int?),
        ]);
        Assert.NotNull(ctor);

        var request = (ChatRequest)ctor!.Invoke(["u", "s", "m", 0.2, true, 100, "p", 7]);
        Assert.Equal(new ChatRequest("u", "s", "m", 0.2, true, 100, "p", 7, null), request);
        Assert.Null(request.ReasoningEffort);
    }

    [Fact]
    public void Named_and_short_calls_still_resolve()
    {
        Assert.Equal("low", new ChatRequest("u", ReasoningEffort: "low").ReasoningEffort);
        Assert.Null(new ChatRequest("u", "s").ReasoningEffort);
    }
}
