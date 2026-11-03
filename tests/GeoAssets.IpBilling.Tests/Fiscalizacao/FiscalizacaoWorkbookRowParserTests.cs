using FluentAssertions;
using GeoAssets.IpBilling.Fiscalizacao;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Fiscalizacao;

public class FiscalizacaoWorkbookRowParserTests
{
    private static readonly string?[] Header = ["Código de Instalação", "Grupo de Operação", "Quantidade"];

    [Fact]
    public void Parse_SkipsHeaderRow_ParsesValidRows()
    {
        var raw = new List<IReadOnlyList<string?>>
        {
            Header,
            new string?[] { "INST-001", "1", "10" },
            new string?[] { "INST-002", "3", "5" },
        };

        var result = FiscalizacaoWorkbookRowParser.Parse(raw);

        result.Errors.Should().BeEmpty();
        result.Rows.Should().BeEquivalentTo(
        [
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10),
            new FiscalizacaoWorkbookRow("INST-002", FiscalizacaoOperationGroup.Novos24hIrregulares, 5),
        ]);
    }

    [Fact]
    public void Parse_MalformedRows_CollectsAllErrorsTogetherNotFailingFast()
    {
        var raw = new List<IReadOnlyList<string?>>
        {
            Header,
            new string?[] { null, "1", "10" },          // missing código
            new string?[] { "INST-002", "9", "10" },    // unrecognized operation group
            new string?[] { "INST-003", "0", "10" },    // Unspecified sentinel is not a valid row value
            new string?[] { "INST-004", "2", "0" },     // non-positive quantity
            new string?[] { "INST-005", "4", "5" },      // valid — should still parse
        };

        var result = FiscalizacaoWorkbookRowParser.Parse(raw);

        result.Errors.Should().HaveCount(4);
        result.Errors.Should().Contain(e => e.RowNumber == 2 && e.Column == "Código de Instalação");
        result.Errors.Should().Contain(e => e.RowNumber == 3 && e.Column == "Grupo de Operação");
        result.Errors.Should().Contain(e => e.RowNumber == 4 && e.Column == "Grupo de Operação");
        result.Errors.Should().Contain(e => e.RowNumber == 5 && e.Column == "Quantidade");
        result.Rows.Should().ContainSingle(r => r.CodigoInstalacao == "INST-005");
    }

    [Fact]
    public void Parse_NoDataRows_ReturnsEmpty()
    {
        var result = FiscalizacaoWorkbookRowParser.Parse([Header]);

        result.Rows.Should().BeEmpty();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Parse_ShortRow_MissingTrailingCellsTreatedAsMissing()
    {
        var raw = new List<IReadOnlyList<string?>>
        {
            Header,
            new string?[] { "INST-001" }, // missing operation-group/quantity cells entirely
        };

        var result = FiscalizacaoWorkbookRowParser.Parse(raw);

        result.Errors.Should().ContainSingle(e => e.RowNumber == 2 && e.Column == "Grupo de Operação");
    }
}
