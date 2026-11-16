using FluentAssertions;
using GeoAssets.IpBilling.Fiscalizacao;
using GeoAssets.IpBilling.Models;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Fiscalizacao;

/// <summary>
/// The optional lamp columns D "Tipo de Lâmpada" / E "Potência (W)" of the Fiscalização
/// spreadsheet (XD01-301): <see cref="FiscalizacaoWorkbookRowParser"/> (IDs UPL-B1..B7) and
/// <see cref="FiscalizacaoWorkbookMapper"/> (IDs UPL-B8..B10, UPL-G1, UPL-G2).
/// </summary>
public class FiscalizacaoWorkbookLampColumnsTests
{
    private static readonly string?[] LegacyHeader = ["Código de Instalação", "Grupo de Operação", "Quantidade"];

    private static readonly string?[] Header =
        ["Código de Instalação", "Grupo de Operação", "Quantidade", "Tipo de Lâmpada", "Potência (W)"];

    private static FiscalizacaoWorkbookParseResult ParseRow(params string?[] cells) =>
        FiscalizacaoWorkbookRowParser.Parse([Header, cells]);

    // ── Parser ────────────────────────────────────────────────────────────────

    [Fact]
    public void Parse_UPL_B1_LegacyThreeColumnSheet_ParsesIdenticallyToBeforeTheLampColumnsExisted()
    {
        var raw = new List<IReadOnlyList<string?>>
        {
            LegacyHeader,
            new string?[] { "INST-001", "1", "10" },
            new string?[] { "INST-001", "2", "8" },
            new string?[] { "INST-002", "3", "5" },
            new string?[] { "INST-003", "4", "1" },
        };

        var result = FiscalizacaoWorkbookRowParser.Parse(raw);

        result.Errors.Should().BeEmpty();
        // Positional 3-argument rows: exactly the shape every pre-XD01-301 caller constructed.
        result.Rows.Should().Equal(
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.UltimoLevantamento, 8),
            new FiscalizacaoWorkbookRow("INST-002", FiscalizacaoOperationGroup.Novos24hIrregulares, 5),
            new FiscalizacaoWorkbookRow("INST-003", FiscalizacaoOperationGroup.Regularizacao24h, 1));
        result.Rows.Should().OnlyContain(r => r.LampCode == null && r.WattageW == null);
    }

    [Fact]
    public void Parse_UPL_B1_LegacySheet_ProducesTheSameRowsAsTheSameSheetWithEmptyLampColumns()
    {
        var legacy = FiscalizacaoWorkbookRowParser.Parse([LegacyHeader, new string?[] { "INST-001", "1", "10" }]);
        var widened = FiscalizacaoWorkbookRowParser.Parse([Header, new string?[] { "INST-001", "1", "10", null, null }]);

        widened.Rows.Should().Equal(legacy.Rows);
        widened.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "\t")]
    [InlineData(null, " ")]
    public void Parse_UPL_B1_BlankOrWhitespaceLampColumns_YieldARowWithoutLampData(string? lamp, string? wattage)
    {
        var result = ParseRow("INST-001", "1", "10", lamp, wattage);

        result.Errors.Should().BeEmpty();
        result.Rows.Should().Equal(new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Parse_UPL_B2_WattageWithoutLampType_ErrorsOnTheLampTypeColumn(string? lamp)
    {
        var result = ParseRow("INST-001", "1", "10", lamp, "70");

        result.Rows.Should().BeEmpty();
        result.Errors.Should().Equal(
            new FiscalizacaoUploadRowError(2, "Tipo de Lâmpada", "Required when Potência (W) is informed."));
    }

    [Fact]
    public void Parse_UPL_B3_LampTypeWithoutWattage_ErrorsOnTheWattageColumn()
    {
        var result = ParseRow("INST-001", "1", "10", "VS");

        result.Rows.Should().BeEmpty();
        result.Errors.Should().Equal(
            new FiscalizacaoUploadRowError(2, "Potência (W)", "Required when Tipo de Lâmpada is informed."));
    }

    [Theory]
    [InlineData("XX")]
    [InlineData("1")]
    [InlineData("VS,VM")]
    public void Parse_UPL_B4_UnrecognizedLampType_ErrorsListingTheValidCodes(string lamp)
    {
        var result = ParseRow("INST-001", "1", "10", lamp, "70");

        result.Rows.Should().BeEmpty();
        result.Errors.Should().Equal(new FiscalizacaoUploadRowError(
            2,
            "Tipo de Lâmpada",
            $"'{lamp}' is not a recognized lamp type (expected one of FL, HL, IN, LD, MT, MV, VM, VS)."));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("abc")]
    [InlineData("70.5")]
    public void Parse_UPL_B5_WattageNotAPositiveInteger_Errors(string wattage)
    {
        var result = ParseRow("INST-001", "1", "10", "VS", wattage);

        result.Rows.Should().BeEmpty();
        result.Errors.Should().Equal(
            new FiscalizacaoUploadRowError(2, "Potência (W)", $"'{wattage}' is not a valid wattage."));
    }

    [Theory]
    [InlineData("VS", "70", LampTypeCode.VS, 70)]
    [InlineData(" vs ", " 70 ", LampTypeCode.VS, 70)]
    [InlineData("Ld", "150", LampTypeCode.LD, 150)]
    public void Parse_UPL_B6_ValidLampColumns_CaseInsensitiveAndTrimmed(
        string lamp, string wattage, LampTypeCode expectedCode, int expectedWattage)
    {
        var result = ParseRow("INST-001", "2", "10", lamp, wattage);

        result.Errors.Should().BeEmpty();
        result.Rows.Should().Equal(new FiscalizacaoWorkbookRow(
            "INST-001", FiscalizacaoOperationGroup.UltimoLevantamento, 10, expectedCode, expectedWattage));
    }

    [Fact]
    public void Parse_UPL_B7_AnEarlierColumnErrorWins_NoLampErrorIsAdded()
    {
        var raw = new List<IReadOnlyList<string?>>
        {
            Header,
            new string?[] { null, "1", "10", "XX", "abc" },        // código
            new string?[] { "INST-002", "9", "10", "XX", "abc" },  // group
            new string?[] { "INST-003", "1", "0", "XX", "abc" },   // quantity
        };

        var result = FiscalizacaoWorkbookRowParser.Parse(raw);

        result.Rows.Should().BeEmpty();
        result.Errors.Select(e => (e.RowNumber, e.Column)).Should().Equal(
            (2, "Código de Instalação"),
            (3, "Grupo de Operação"),
            (4, "Quantidade"));
    }

    [Fact]
    public void Parse_OneErrorPerRow_AndValidRowsStillParse()
    {
        var raw = new List<IReadOnlyList<string?>>
        {
            Header,
            new string?[] { "INST-001", "1", "10", "XX", "abc" }, // both lamp cells bad → only the lamp-type error
            new string?[] { "INST-002", "1", "4", "LD", "60" },
        };

        var result = FiscalizacaoWorkbookRowParser.Parse(raw);

        result.Errors.Should().ContainSingle().Which.Column.Should().Be(FiscalizacaoWorkbookRowParser.LampTypeColumn);
        result.Rows.Should().ContainSingle().Which.LampCode.Should().Be(LampTypeCode.LD);
    }

    // ── Mapper ────────────────────────────────────────────────────────────────

    [Fact]
    public void ToLines_UPL_B8_LampRow_AddsOneOperationGroupDetailAndOneLampDetail()
    {
        var lines = FiscalizacaoWorkbookMapper.ToLines(
            "order-1", [new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 6, LampTypeCode.VS, 100)]);

        var line = lines.Should().ContainSingle().Which;
        line.OperationGroups.Should().ContainSingle(d =>
            d.OperationGroup == FiscalizacaoOperationGroup.Fiscalizacao && d.Count == 6 && d.LineId == line.Id);
        var lamp = line.LampDetails.Should().ContainSingle().Which;
        lamp.LineId.Should().Be(line.Id);
        lamp.OperationGroup.Should().Be(FiscalizacaoOperationGroup.Fiscalizacao);
        lamp.Code.Should().Be(LampTypeCode.VS);
        lamp.WattageW.Should().Be(100);
        lamp.Quantity.Should().Be(6);
    }

    [Fact]
    public void ToLines_LamplessRow_AddsNoLampDetail()
    {
        var lines = FiscalizacaoWorkbookMapper.ToLines(
            "order-1", [new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 6)]);

        lines.Single().LampDetails.Should().BeEmpty();
    }

    [Fact]
    public void ToLines_UPL_G1_RowWithOnlyLampTypeOrOnlyWattage_Throws()
    {
        var onlyLamp = () => FiscalizacaoWorkbookMapper.ToLines(
            "order-1", [new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 6, LampTypeCode.VS)]);
        var onlyWattage = () => FiscalizacaoWorkbookMapper.ToLines(
            "order-1", [new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 6, null, 100)]);

        onlyLamp.Should().Throw<ArgumentException>().WithMessage("*INST-001*");
        onlyWattage.Should().Throw<ArgumentException>().WithMessage("*INST-001*");
    }

    [Fact]
    public void ToRows_UPL_B9_LineWithoutLampDetails_OneLegacyRowPerOperationGroupDetail()
    {
        var lines = FiscalizacaoWorkbookMapper.ToLines("order-1",
        [
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.UltimoLevantamento, 8),
        ]);

        FiscalizacaoWorkbookMapper.ToRows(lines).Should().Equal(
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 10),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.UltimoLevantamento, 8));
    }

    [Fact]
    public void ToRows_UPL_B10_LineWithLampDetails_LampRowsPlusRemainder_AndRoundTripPreservesTotalsAndLampDetails()
    {
        var original = FiscalizacaoWorkbookMapper.ToLines("order-1",
        [
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 6, LampTypeCode.VS, 100),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 4),                        // remainder
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.UltimoLevantamento, 3, LampTypeCode.LD, 60), // fully covered
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Novos24hIrregulares, 2),                 // no lamp at all
        ]);
        foreach (var line in original) FiscalizacaoLineCalculator.Recalculate(line);

        var rows = FiscalizacaoWorkbookMapper.ToRows(original);

        rows.Should().Equal(
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 6, LampTypeCode.VS, 100),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Fiscalizacao, 4),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.UltimoLevantamento, 3, LampTypeCode.LD, 60),
            new FiscalizacaoWorkbookRow("INST-001", FiscalizacaoOperationGroup.Novos24hIrregulares, 2));

        var roundTripped = FiscalizacaoWorkbookMapper.ToLines("order-1", rows);
        foreach (var line in roundTripped) FiscalizacaoLineCalculator.Recalculate(line);

        roundTripped.Single().Agregado.Should().Be(original.Single().Agregado);
        roundTripped.Single().Retroativo.Should().Be(original.Single().Retroativo);
        roundTripped.Single().LampDetails.Select(l => (l.OperationGroup, l.Code, l.WattageW, l.Quantity))
            .Should().Equal(original.Single().LampDetails.Select(l => (l.OperationGroup, l.Code, l.WattageW, l.Quantity)));
    }

    [Fact]
    public void ToRows_UPL_G2_NullLines_Throws()
    {
        var act = () => FiscalizacaoWorkbookMapper.ToRows(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToRows_UPL_G2_LampDetailExceedingItsGroupCount_Throws()
    {
        var line = new FiscalizacaoLine { ServiceOrderId = "order-1", CodigoInstalacao = "INST-009" };
        line.OperationGroups.Add(new FiscalizacaoOperationGroupDetail
        {
            LineId = line.Id,
            OperationGroup = FiscalizacaoOperationGroup.Fiscalizacao,
            Count = 2,
        });
        line.LampDetails.Add(new FiscalizacaoLampDetail
        {
            LineId = line.Id,
            OperationGroup = FiscalizacaoOperationGroup.Fiscalizacao,
            Code = LampTypeCode.VS,
            WattageW = 100,
            Quantity = 3,
        });

        var act = () => FiscalizacaoWorkbookMapper.ToRows([line]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*INST-009*");
    }
}
