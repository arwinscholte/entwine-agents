using EntwineAgents.Intake;
using FluentAssertions;

namespace EntwineAgents.IntakeTest;

/// <summary>
/// The shape classifier reads a table for what it is. Deterministic first — exact names, synonyms, whole-word
/// containment, value shape — and only the residue (nothing fits, or a tie) goes to a model, whose answer is
/// validated against the schemas and the headers so it cannot invent either.
/// </summary>
public class TableShapeClassifierTests
{
    private static bool LooksLikeDates(IReadOnlyList<string> v) => v.Count > 0 && v.Count(x => DateTime.TryParse(x, out _)) * 2 >= v.Count;
    private static bool LooksLikeRating(IReadOnlyList<string> v) => v.Count > 0 && v.All(x => decimal.TryParse(x, out var d) && d is >= 0 and <= 10);

    private static readonly TargetSchema Engagements = new("Engagements", new[]
    {
        SchemaColumn.Of("Partner", required: true, "Partner name", "Reseller", "Vendor partner"),
        SchemaColumn.Of("Customer", required: true, "Account", "Client", "Account name"),
        SchemaColumn.Of("Engagement", required: true, "Description", "Service line", "Work"),
        new("Start", new[] { "Start date", "Kick-off", "From" }, ValueShape: LooksLikeDates),
        new("End", new[] { "End date", "Thru", "To" }, ValueShape: LooksLikeDates),
    }, "one row per piece of partner work on a customer");

    private static readonly TargetSchema Sourcing = new("Sourcing", new[]
    {
        SchemaColumn.Of("Partner", required: true, "Partner name", "Referrer"),
        SchemaColumn.Of("Customer", required: true, "Account", "Prospect"),
        SchemaColumn.Of("Deal", required: true, "Opportunity", "Opportunity name", "Deal name"),
    }, "one row per deal a partner sourced or influenced");

    private static readonly TargetSchema Outcomes = new("Outcomes", new[]
    {
        SchemaColumn.Of("Customer", required: true, "Account", "Client"),
        SchemaColumn.Of("Status", required: true, "Lifecycle", "Account status"),
        new("CSAT", new[] { "Satisfaction", "NPS" }, ValueShape: LooksLikeRating),
    }, "one row per customer with its health");

    private static TableShapeClassifier Sut() => new(new[] { Engagements, Sourcing, Outcomes });

    private static RecordTableReader.Table Table(string csv) => RecordTableReader.ReadCsv(new StringReader(csv));

    [Fact]
    public void Exact_headers_read_as_that_schema_with_no_model()
    {
        var r = Sut().Classify(Table("Partner,Customer,Engagement,Start,End\nApex,Acme,Onboarding,2025-01-01,2025-03-01"));

        r.IsUnknown.Should().BeFalse();
        r.Best!.Schema.Name.Should().Be("Engagements");
        r.Best.Score.Should().Be(1.0);
        r.Best.Mappings.Should().OnlyContain(m => m.Basis == "exact");
        r.UsedModel.Should().BeFalse();
    }

    [Fact]
    public void Synonyms_and_whole_word_containment_bind_the_headers_a_customer_actually_uses()
    {
        var r = Sut().Classify(Table("Reseller,Account name,Service line,Kick-off (planned)\nApex,Acme,Onboarding,2025-01-01"));

        r.Best!.Schema.Name.Should().Be("Engagements");
        r.Best.HeaderFor("Partner").Should().Be("Reseller");
        r.Best.HeaderFor("Customer").Should().Be("Account name");
        r.Best.HeaderFor("Engagement").Should().Be("Service line");
        r.Best.HeaderFor("Start").Should().Be("Kick-off (planned)");
        r.Best.Mappings.Single(m => m.Column == "Start").Basis.Should().Be("contains");
        r.Best.Describe().Should().Be("Partner = Reseller · Customer = Account name · Engagement = Service line · Start = Kick-off (planned)");
    }

    [Fact]
    public void Value_shape_binds_a_column_the_headers_do_not_name()
    {
        var r = Sut().Classify(Table("Account,Lifecycle,Q3 score\nAcme,Healthy,8\nGlobex,At risk,4\nInitech,Healthy,9"));

        r.Best!.Schema.Name.Should().Be("Outcomes");
        r.Best.HeaderFor("CSAT").Should().Be("Q3 score");
        r.Best.Mappings.Single(m => m.Column == "CSAT").Basis.Should().Be("shape");
    }

    [Fact]
    public void A_pipeline_export_is_read_as_sourcing_whatever_slot_it_came_in()
    {
        var r = Sut().Classify(Table("Partner,Account,Opportunity name\nApex,Acme,Payroll expansion"));

        r.Best!.Schema.Name.Should().Be("Sourcing");
        r.Candidates.Select(c => c.Schema.Name).First().Should().Be("Sourcing");
        r.Candidates.Single(c => c.Schema.Name == "Engagements").Complete.Should().BeFalse("no engagement column");
    }

    [Fact]
    public void Each_header_binds_at_most_once_and_the_projection_re_heads_the_table()
    {
        var table = Table("Partner name,Account,Work,Notes\nApex,Acme,Onboarding,fine");
        var r = Sut().Classify(table);

        r.Best!.Mappings.Select(m => m.Header).Should().OnlyHaveUniqueItems();
        var projected = r.Best.Project(table);
        projected.Headers.Should().Equal("Partner", "Customer", "Engagement", "Notes");   // unbound headers keep their name
        projected.Cell(projected.Rows[0], "Customer").Should().Be("Acme");
    }

    [Fact]
    public void Nothing_fits_is_unknown_not_a_guess()
    {
        var r = Sut().Classify(Table("Variable,Value,Unit\nN_CUSTOMERS,200,customers"));

        r.IsUnknown.Should().BeTrue();
        r.Note.Should().Contain("No schema fits");
        r.Candidates.Should().OnlyContain(c => !c.Complete);
    }

    [Fact]
    public async Task Residue_makes_one_model_call_and_takes_a_validated_answer()
    {
        var calls = 0;
        string? seen = null;
        ShapeResidueCall residue = (prompt, _) => { calls++; seen = prompt; return Task.FromResult<string?>(
            "```json\n{\"schema\":\"Engagements\",\"columns\":{\"Partner\":\"Firm\",\"Customer\":\"Logo\",\"Engagement\":\"What we did\",\"Start\":\"Nope\",\"Imaginary\":\"Firm\"}}\n```"); };

        var r = await Sut().ClassifyAsync(Table("Firm,Logo,What we did\nApex,Acme Corp,Onboarding"), residue, scrub: s => s.Replace("Acme Corp", "ACCOUNT_01"));

        calls.Should().Be(1);
        seen.Should().Contain("ACCOUNT_01").And.NotContain("Acme Corp", "sample rows are scrubbed before the prompt");
        r.UsedModel.Should().BeTrue();
        r.Best!.Schema.Name.Should().Be("Engagements");
        r.Best.HeaderFor("Partner").Should().Be("Firm");
        r.Best.HeaderFor("Engagement").Should().Be("What we did");
        r.Best.Has("Start").Should().BeFalse("'Nope' is not a header of the table — dropped");
        r.Best.Mappings.Should().NotContain(m => m.Column == "Imaginary", "not a column of the schema — dropped");
        r.Best.Mappings.Should().OnlyContain(m => m.Basis == "model" && m.Confidence == 0.6);
    }

    [Fact]
    public async Task A_fabricated_schema_or_garbage_leaves_the_table_unknown()
    {
        var r1 = await Sut().ClassifyAsync(Table("Firm,Logo\nApex,Acme"), (_, _) => Task.FromResult<string?>("{\"schema\":\"Invoices\",\"columns\":{}}"));
        r1.IsUnknown.Should().BeTrue();
        r1.UsedModel.Should().BeTrue();

        var r2 = await Sut().ClassifyAsync(Table("Firm,Logo\nApex,Acme"), (_, _) => Task.FromResult<string?>("I think it is an engagement list."));
        r2.IsUnknown.Should().BeTrue();
        r2.Note.Should().Contain("no usable answer");
    }

    [Fact]
    public async Task A_tie_goes_to_the_model_and_a_clear_reading_never_does()
    {
        // Partner + Customer only: Engagements, Sourcing and Outcomes all bind what they can; none is complete → unknown, so the model is asked.
        var calls = 0;
        ShapeResidueCall residue = (_, _) => { calls++; return Task.FromResult<string?>("{\"schema\":\"Sourcing\",\"columns\":{\"Deal\":\"Ref\"}}"); };
        var r = await Sut().ClassifyAsync(Table("Partner,Customer,Ref\nApex,Acme,Q3-17"), residue);
        calls.Should().Be(1);
        r.Best!.Schema.Name.Should().Be("Sourcing");
        r.Best.Mappings.Select(m => m.Basis).Should().BeEquivalentTo(new[] { "exact", "exact", "model" });

        calls = 0;
        var clear = await Sut().ClassifyAsync(Table("Partner,Customer,Engagement\nApex,Acme,Onboarding"), residue);
        calls.Should().Be(0, "a clear deterministic reading never spends a call");
        clear.UsedModel.Should().BeFalse();
    }

    [Fact]
    public void The_prompt_lists_the_schemas_the_headers_and_the_asked_for_json()
    {
        var prompt = Sut().BuildPrompt(Table("Firm,Logo\nApex,Acme"), null);
        prompt.Should().Contain("- Engagements: one row per piece of partner work")
            .And.Contain("Partner (required) — also called Partner name, Reseller, Vendor partner")
            .And.Contain("Table headers: Firm | Logo")
            .And.Contain("{\"schema\": \"<schema name>\", \"columns\"");
    }
}
