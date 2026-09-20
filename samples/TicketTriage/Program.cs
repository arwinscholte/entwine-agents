// TicketTriage: a messy export in, a triaged table out — and the same run with no API key.
//
// What it shows, in order:
//   1. Read the file for what it is — the CSV uses someone else's headers; TableShapeClassifier binds them.
//   2. Take the names out — PseudonymMap swaps customers for tokens before anything reaches the model.
//   3. Classify in batches, typed — BatchAgent returns one Triage per ticket; a mangled batch degrades per item.
//   4. Put the names back and print the table.
//   Run it again without OPENAI_API_KEY: every ticket comes back Unclassified, the process still exits 0.
//
//   set OPENAI_API_KEY=sk-...        (or export on unix; optionally OPENAI_BASE_URL / OPENAI_MODEL)
//   dotnet run --project samples/TicketTriage                 # uses the tickets.csv beside this file
//   dotnet run --project samples/TicketTriage -- yours.csv    # or your own export

using System.Text;
using System.Text.Json;
using EntwineAgents.Ai;
using EntwineAgents.Intake;
using EntwineAgents.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// ── 1. Read the file for what it is ──────────────────────────────────────────────────────────────────────────
var path = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "tickets.csv");
var table = RecordTableReader.ReadCsv(new StreamReader(path));

// The columns this program understands, and the names an export might give them. The file's headers are
// "Who / What they wrote / Logged / Plan" — none of them ours. The classifier binds by name, synonym or
// containment and tells us how; a column it cannot bind stays unbound rather than guessed.
var tickets = new TargetSchema("tickets", new[]
{
    SchemaColumn.Of("Customer", required: true, "Account", "Client", "Company", "Who", "From"),
    SchemaColumn.Of("Message", required: true, "Body", "Description", "Text", "What they wrote", "Ticket"),
    new SchemaColumn("Opened", new[] { "Date", "Created", "Logged", "Received" }, ValueShape: v => v.Count(x => DateTime.TryParse(x, out _)) * 2 >= v.Count),
    SchemaColumn.Of("Plan", required: false, "Tier", "Subscription", "Package"),
});
var shape = new TableShapeClassifier(new[] { tickets }).Classify(table);
if (shape.IsUnknown)
{
    Console.WriteLine($"Could not read {Path.GetFileName(path)} as a ticket export: {shape.Note}");
    return;
}
Console.WriteLine($"Read as {shape.Best!.Schema.Name}: {shape.Best.Describe()}");
var rows = shape.Best.Project(table);   // headers are now Customer / Message / Opened / Plan

// ── 2. Take the names out ───────────────────────────────────────────────────────────────────────────────────
// Each customer becomes ACCOUNT_nn for this session; the map lives in memory and dies with the process.
var names = new PseudonymMap();
var input = rows.Rows.Select(r => new Ticket(
        Customer: names.AnonymizeAccount(rows.Cell(r, "Customer")),
        Message: names.Scrub(rows.Cell(r, "Message")),
        Opened: rows.Cell(r, "Opened"),
        Plan: rows.Cell(r, "Plan")))
    .ToList();
Console.WriteLine($"\n{input.Count} tickets, {names.Count} customer names replaced before the model sees anything, e.g. \"{input[0].Customer}: {Cut(input[0].Message, 60)}\"\n");

// ── 3. Classify in batches, typed ───────────────────────────────────────────────────────────────────────────
var chat = BuildChat();   // null when OPENAI_API_KEY is not set: the agent still runs, every item degrades
var agent = new TriageAgent(chat ?? new NoModel());
var results = await agent.RunAsync(input);

// ── 4. Put the names back and print ─────────────────────────────────────────────────────────────────────────
Console.WriteLine($"{"Customer",-22} {"Urgency",-8} {"Category",-14} Next action");
Console.WriteLine(new string('─', 96));
foreach (var (ticket, triage) in input.Zip(results).OrderByDescending(x => x.Second.Rank))
    Console.WriteLine($"{names.Hydrate(ticket.Customer),-22} {triage.Urgency,-8} {triage.Category,-14} {names.Hydrate(triage.NextAction)}");   // tokens in the action text come back too

var unclassified = results.Count(r => r.Category == "Unclassified");
Console.WriteLine(unclassified == 0
    ? $"\nLook first at: {string.Join(", ", input.Zip(results).OrderByDescending(x => x.Second.Rank).Take(3).Select(x => names.Hydrate(x.First.Customer)))}."
    : $"\n{unclassified} of {results.Count} tickets could not be classified{(chat is null ? " — no OPENAI_API_KEY, so the model was never called" : "")}; they are marked, not dropped, and the run still completed.");

// ── plumbing ─────────────────────────────────────────────────────────────────────────────────────────────────
static IAgentChat? BuildChat()
{
    var apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    if (string.IsNullOrWhiteSpace(apiKey)) return null;
    var baseUrl = Environment.GetEnvironmentVariable("OPENAI_BASE_URL") ?? "https://api.openai.com/v1/";
    var model = Environment.GetEnvironmentVariable("OPENAI_MODEL") ?? "gpt-4.1-nano";
    var services = new ServiceCollection();
    services.AddHttpClient("LLM", c =>
    {
        c.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        c.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
    });
    var sp = services.BuildServiceProvider();
    var provider = new OpenAiCompatibleChatProvider(sp.GetRequiredService<IHttpClientFactory>(), Options.Create(new LlmOptions { ModelId = model }));
    return new ChatProviderAgentChat(provider);   // the provider seam bridged to the agent seam
}

static string Cut(string s, int n) => s.Length <= n ? s : s[..n] + "…";

record Ticket(string Customer, string Message, string Opened, string Plan);

/// <summary>One ticket's triage. <see cref="Rank"/> orders the table; Unclassified sorts last.</summary>
record Triage(string Category, string Urgency, string NextAction)
{
    public static Triage Unclassified => new("Unclassified", "—", "Read by hand — the model gave no usable answer for this one.");
    public int Rank => Category == "Unclassified" ? -1 : Urgency switch { "high" => 3, "medium" => 2, "low" => 1, _ => 0 };
}

/// <summary>
/// The batched shell does the work: chunks of twelve, one retry on a bad answer, output aligned to input by
/// position, and a batch the model still mangles degrades one item at a time to <see cref="Triage.Unclassified"/>.
/// The four overrides are all that is ours: the prompt, how a batch is rendered, how the answer is parsed, and
/// what a failed item becomes.
/// </summary>
sealed class TriageAgent(IAgentChat chat) : BatchAgent<Ticket, Triage>(chat)
{
    protected override string Key => "sample.ticket-triage.system";   // a host can override the prompt by key
    protected override string FallbackPrompt =>
        "You triage customer support tickets for a B2B software company. For each ticket give a category " +
        "(one of: outage, bug, billing, feature request, how-to, security, churn risk, expansion, compliance, thanks), " +
        "an urgency (high, medium, low) and a one-sentence next action for the support lead. " +
        "Answer ONLY with JSON: {\"tickets\": [{\"category\": \"...\", \"urgency\": \"...\", \"next_action\": \"...\"}]} " +
        "with exactly one object per input ticket, in the same order.";

    protected override string BuildUserPrompt(IReadOnlyList<Ticket> batch)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < batch.Count; i++)
            sb.AppendLine($"[{i}] customer={batch[i].Customer} plan={batch[i].Plan} opened={batch[i].Opened} message=\"{batch[i].Message.Replace('"', '\'')}\"");
        return sb.ToString();
    }

    protected override bool TryParse(string raw, IReadOnlyList<Ticket> batch, out List<Triage> parsed)
    {
        parsed = [];
        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("tickets", out var arr) || arr.ValueKind != JsonValueKind.Array) return false;
            var items = arr.EnumerateArray().ToList();
            for (var i = 0; i < batch.Count; i++)
                parsed.Add(i < items.Count && items[i].ValueKind == JsonValueKind.Object
                    ? new Triage(Str(items[i], "category"), Str(items[i], "urgency").ToLowerInvariant(), Str(items[i], "next_action"))
                    : Triage.Unclassified);   // the model skipped one: that one degrades, the rest stand
            return true;
        }
        catch (JsonException) { return false; }   // unparseable: the shell retries once, then Flagged() per item
    }

    protected override Triage Flagged(Ticket item) => Triage.Unclassified;

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}

/// <summary>What the agent talks to when there is no key: every call fails, and the shell's degrade path shows itself.</summary>
sealed class NoModel : IAgentChat
{
    public Task<string> CompleteAsync(ChatTurn turn, CancellationToken ct = default) => throw new InvalidOperationException("OPENAI_API_KEY is not set");
}
