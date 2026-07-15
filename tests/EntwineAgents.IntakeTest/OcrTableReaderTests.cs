using EntwineAgents.Intake;
using FluentAssertions;

namespace EntwineAgents.ConciergeTest.Intake;

/// <summary>PDF intake, stage 1: EntwineAgents.Ocr renders detected tables as [TABLE] markdown;
/// ReadOcrText parses that into the same Table the XLSX/CSV paths use (same header detection).</summary>
public class OcrTableReaderTests
{
    private const string OcrText = """
        Quarterly Partner Report
        Some narrative paragraph the OCR extracted.

        [TABLE]
        | Partner | Customer | Engagement | Start |
        | --- | --- | --- | --- |
        | DCH | BMC Stock | Post implementation Support | 2019-01-01 |
        | DCH | San Ysidro | Post implementation Support | 2019-02-01 |
        [/TABLE]

        Page 2

        [TABLE]
        | Partner | Customer | Engagement | Start |
        | --- | --- | --- | --- |
        | DCH | Del Monte | Post implementation Support | 2019-03-01 |
        [/TABLE]
        """;

    [Fact]
    public void Parses_table_blocks_skipping_separators_and_page_repeated_headers()
    {
        var table = RecordTableReader.ReadOcrText(OcrText);

        table.Headers.Should().ContainInOrder("Partner", "Customer", "Engagement", "Start");
        table.Rows.Should().HaveCount(3);                        // 2 + 1, header repeated on page 2 dropped
        table.Cell(table.Rows[2], "Customer").Should().Be("Del Monte");
    }

    [Fact]
    public void Narrative_text_outside_table_blocks_is_ignored()
    {
        var table = RecordTableReader.ReadOcrText(OcrText);

        table.Rows.Should().OnlyContain(r => table.Cell(r, "Partner") == "DCH");
    }

    [Fact]
    public void No_table_blocks_yields_an_empty_table()
    {
        var table = RecordTableReader.ReadOcrText("Just a letter with no tables at all.");

        table.Headers.Should().BeEmpty();
        table.Rows.Should().BeEmpty();
    }
}
