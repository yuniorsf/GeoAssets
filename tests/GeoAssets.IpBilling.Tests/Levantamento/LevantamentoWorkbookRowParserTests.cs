using FluentAssertions;
using GeoAssets.IpBilling.Levantamento;
using GeoAssets.IpBilling.Models;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Levantamento;

public class LevantamentoWorkbookRowParserTests
{
    private static readonly string?[] Header = ["Código de Instalação", "Tipo de Lâmpada", "Potência (W)", "Quantidade"];

    [Fact]
    public void Parse_SkipsHeaderRow_ParsesValidRows()
    {
        var raw = new List<IReadOnlyList<string?>>
        {
            Header,
            new string?[] { "INST-001", "LD", "100", "10" },
            new string?[] { "INST-002", "VS", "250", "5" },
        };

        var result = LevantamentoWorkbookRowParser.Parse(raw);

        result.Errors.Should().BeEmpty();
        result.Rows.Should().BeEquivalentTo(
        [
            new LevantamentoWorkbookRow("INST-001", LampTypeCode.LD, 100, 10),
            new LevantamentoWorkbookRow("INST-002", LampTypeCode.VS, 250, 5),
        ]);
    }

    [Fact]
    public void Parse_MalformedRows_CollectsAllErrorsTogetherNotFailingFast()
    {
        var raw = new List<IReadOnlyList<string?>>
        {
            Header,
            new string?[] { null, "LD", "100", "10" },              // missing código
            new string?[] { "INST-002", "NOPE", "100", "10" },      // bad lamp type
            new string?[] { "INST-003", "LD", "abc", "10" },        // bad wattage
            new string?[] { "INST-004", "LD", "100", "0" },         // non-positive quantity
            new string?[] { "INST-005", "LD", "100", "5" },         // valid — should still parse
        };

        var result = LevantamentoWorkbookRowParser.Parse(raw);

        result.Errors.Should().HaveCount(4);
        result.Errors.Should().Contain(e => e.RowNumber == 2 && e.Column == "Código de Instalação");
        result.Errors.Should().Contain(e => e.RowNumber == 3 && e.Column == "Tipo de Lâmpada");
        result.Errors.Should().Contain(e => e.RowNumber == 4 && e.Column == "Potência (W)");
        result.Errors.Should().Contain(e => e.RowNumber == 5 && e.Column == "Quantidade");
        result.Rows.Should().ContainSingle(r => r.CodigoInstalacao == "INST-005");
    }

    [Fact]
    public void Parse_NoDataRows_ReturnsEmpty()
    {
        var result = LevantamentoWorkbookRowParser.Parse([Header]);

        result.Rows.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ShortRow_MissingTrailingCellsTreatedAsMissing()
    {
        var raw = new List<IReadOnlyList<string?>>
        {
            Header,
            new string?[] { "INST-001", "LD" }, // missing wattage/quantity cells entirely
        };

        var result = LevantamentoWorkbookRowParser.Parse(raw);

        result.Errors.Should().ContainSingle(e => e.RowNumber == 2 && e.Column == "Potência (W)");
    }
}
