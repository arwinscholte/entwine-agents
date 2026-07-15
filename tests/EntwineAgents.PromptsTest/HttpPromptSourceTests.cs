using System.Net;
using System.Text;
using EntwineAgents.Prompts;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace EntwineAgentsTest.Services;

/// <summary>
/// The DB-free prompt binding for satellite hosts: resolve over the prompt-egress endpoint, fail OPEN to the
/// built-in fallback (an egress outage must never take an agent down), and cache resolutions.
/// </summary>
public class HttpPromptSourceTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public HttpRequestMessage? LastRequest { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private static HttpClient Client(StubHandler handler) =>
        new(handler) { BaseAddress = new Uri("https://app.example/") };

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task Resolves_the_stored_prompt_from_the_egress_endpoint()
    {
        var handler = new StubHandler(_ => Json("""{"promptKey":"k","promptText":"STORED PROMPT","modelId":null}"""));

        var text = await new HttpPromptSource(Client(handler)).GetAsync("engagement.risk.system", "fallback");

        Assert.Equal("STORED PROMPT", text);
        Assert.Equal("/api/prompt-egress/engagement.risk.system", handler.LastRequest!.RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task A_404_means_no_override_and_the_fallback_is_used()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var text = await new HttpPromptSource(Client(handler)).GetAsync("k", "the built-in prompt");

        Assert.Equal("the built-in prompt", text);
    }

    [Fact]
    public async Task Failures_fail_open_to_the_fallback_and_are_not_cached()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("egress down"));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var source = new HttpPromptSource(Client(handler), cache);

        Assert.Equal("fallback", await source.GetAsync("k", "fallback"));
        Assert.Equal("fallback", await source.GetAsync("k", "fallback"));
        Assert.Equal(2, handler.Calls);   // failure not cached — the next call retries
    }

    [Fact]
    public async Task Resolutions_are_cached_so_hot_paths_skip_the_network()
    {
        var handler = new StubHandler(_ => Json("""{"promptKey":"k","promptText":"STORED","modelId":null}"""));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var source = new HttpPromptSource(Client(handler), cache);

        await source.GetAsync("k", "fallback");
        var second = await source.GetAsync("k", "fallback");

        Assert.Equal("STORED", second);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task No_override_is_cached_too_but_still_yields_the_fallback()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var cache = new MemoryCache(new MemoryCacheOptions());
        var source = new HttpPromptSource(Client(handler), cache);

        Assert.Equal("fallback", await source.GetAsync("k", "fallback"));
        Assert.Equal("fallback", await source.GetAsync("k", "fallback"));
        Assert.Equal(1, handler.Calls);   // the "no override" answer is cached
    }
}
