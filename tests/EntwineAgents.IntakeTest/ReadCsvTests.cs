using EntwineAgents.Intake;
using FluentAssertions;

namespace EntwineAgents.ConciergeTest.Intake;

/// <summary>CSV reading — in particular quoted fields containing embedded newlines (legal CSV, common in
/// exported description columns), which must parse as one cell of one row (issue #1).</summary>
public class ReadCsvTests
{
    private static RecordTableReader.Table Read(string csv) => RecordTableReader.ReadCsv(new StringReader(csv));

    [Fact]
    public void Quoted_field_with_embedded_newline_is_one_cell_of_one_row()
    {
        var table = Read("Customer,Description\nAcme,\"line one\nline two\"\nGlobex,plain");

        table.Rows.Should().HaveCount(2);
        table.Cell(table.Rows[0], "Description").Should().Be("line one\nline two");
        table.Cell(table.Rows[1], "Customer").Should().Be("Globex");   // the next row is intact
    }

    [Fact]
    public void Embedded_newline_and_comma_inside_quotes_stay_in_the_cell()
    {
        var table = Read("Customer,Description\nAcme,\"renewal, phase 2\nkick-off pending\"");

        var row = table.Rows.Single();
        table.Cell(row, "Description").Should().Be("renewal, phase 2\nkick-off pending");
    }

    [Fact]
    public void Plain_files_and_quoted_commas_parse_exactly_as_before()
    {
        var table = Read("Customer,Description\n\"Acme, Inc\",migration\nGlobex,support");

        table.Rows.Should().HaveCount(2);
        table.Cell(table.Rows[0], "Customer").Should().Be("Acme, Inc");
        table.Cell(table.Rows[1], "Description").Should().Be("support");
    }
}
