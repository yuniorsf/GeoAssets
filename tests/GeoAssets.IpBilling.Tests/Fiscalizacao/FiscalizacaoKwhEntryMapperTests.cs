using FluentAssertions;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Fiscalizacao;
using GeoAssets.IpBilling.Models;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Fiscalizacao;

/// <summary>Covers every guard/branch of <see cref="FiscalizacaoKwhEntryMapper"/> (XD01-301, IDs MAP-*).</summary>
public class FiscalizacaoKwhEntryMapperTests
{
    private static FiscalizacaoLine Line(
        (FiscalizacaoOperationGroup Group, int Count)[] groups,
        params (FiscalizacaoOperationGroup Group, LampTypeCode Code, int WattageW, int Quantity)[] lamps)
    {
        var line = new FiscalizacaoLine { ServiceOrderId = "order-1", CodigoInstalacao = "INST-001" };
        line.OperationGroups.AddRange(groups.Select(g => new FiscalizacaoOperationGroupDetail
        {
            LineId = line.Id,
            OperationGroup = g.Group,
            Count = g.Count,
        }));
        line.LampDetails.AddRange(lamps.Select(l => new FiscalizacaoLampDetail
        {
            LineId = line.Id,
            OperationGroup = l.Group,
            Code = l.Code,
            WattageW = l.WattageW,
            Quantity = l.Quantity,
        }));
        return line;
    }

    [Fact]
    public void ToEntries_MAP_G1_NullLines_Throws()
    {
        var act = () => FiscalizacaoKwhEntryMapper.ToEntries(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CountWithoutLampDetail_MAP_G1_NullLine_Throws()
    {
        var act = () => FiscalizacaoKwhEntryMapper.CountWithoutLampDetail(null!, FiscalizacaoOperationGroup.Fiscalizacao);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToEntries_MAP_G2_LampQuantityExceedsGroupCount_ThrowsNamingCodigoAndGroup()
    {
        var line = Line(
            [(FiscalizacaoOperationGroup.Fiscalizacao, 5)],
            (FiscalizacaoOperationGroup.Fiscalizacao, LampTypeCode.VS, 100, 6));

        var act = () => FiscalizacaoKwhEntryMapper.ToEntries([line]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*INST-001*").WithMessage("*Fiscalizacao*");
    }

    [Fact]
    public void ToEntries_MAP_B1_NoLampDetails_OneLamplessEntryPerNonEmptyGroup_WithSignAnd24hFlag()
    {
        var line = Line(
        [
            (FiscalizacaoOperationGroup.Fiscalizacao, 10),
            (FiscalizacaoOperationGroup.UltimoLevantamento, 8),
            (FiscalizacaoOperationGroup.Novos24hIrregulares, 4),
            (FiscalizacaoOperationGroup.Regularizacao24h, 3),
        ]);

        var entries = FiscalizacaoKwhEntryMapper.ToEntries([line]);

        entries.Select(e => (e.Quantity, e.Sign, e.IsIrregular24h, e.HasLampDetail)).Should().Equal(
            (10, 1, false, false),
            (8, -1, false, false),
            (4, 1, true, false),
            (3, -1, true, false));
        entries.Take(2).Should().OnlyContain(e => e.CountsTowardRetroativo, "groups 1/2 are always retroactive");
    }

    [Fact]
    public void ToEntries_MAP_B2_LampDetailsCoverTheWholeGroup_OnlyExactEntries()
    {
        var line = Line(
            [(FiscalizacaoOperationGroup.Fiscalizacao, 10)],
            (FiscalizacaoOperationGroup.Fiscalizacao, LampTypeCode.VS, 100, 6),
            (FiscalizacaoOperationGroup.Fiscalizacao, LampTypeCode.LD, 60, 4));

        var entries = FiscalizacaoKwhEntryMapper.ToEntries([line]);

        entries.Should().Equal(
            new InspectionPointEntry(6, 1, false, true, LampTypeCode.VS, 100),
            new InspectionPointEntry(4, 1, false, true, LampTypeCode.LD, 60));
        FiscalizacaoKwhEntryMapper.CountWithoutLampDetail(line, FiscalizacaoOperationGroup.Fiscalizacao).Should().Be(0);
    }

    [Fact]
    public void ToEntries_MAP_B3_PartialCover_ExactEntriesPlusOneRemainderEntry()
    {
        var line = Line(
            [(FiscalizacaoOperationGroup.UltimoLevantamento, 10)],
            (FiscalizacaoOperationGroup.UltimoLevantamento, LampTypeCode.VS, 100, 6));

        var entries = FiscalizacaoKwhEntryMapper.ToEntries([line]);

        entries.Should().Equal(
            new InspectionPointEntry(6, -1, false, true, LampTypeCode.VS, 100),
            new InspectionPointEntry(4, -1, false, true));
        FiscalizacaoKwhEntryMapper.CountWithoutLampDetail(line, FiscalizacaoOperationGroup.UltimoLevantamento).Should().Be(4);
    }

    [Fact]
    public void ToEntries_MAP_B4_EmptyOrUnspecifiedGroup_ProducesNoEntry()
    {
        var line = Line(
        [
            (FiscalizacaoOperationGroup.Unspecified, 7),
            (FiscalizacaoOperationGroup.Fiscalizacao, 0),
        ]);

        FiscalizacaoKwhEntryMapper.ToEntries([line]).Should().BeEmpty();
        FiscalizacaoKwhEntryMapper.ToEntries([]).Should().BeEmpty();
    }

    [Fact]
    public void ToEntries_MAP_B5_Groups3And4_NeverCountTowardRetroativo()
    {
        var line = Line(
            [
                (FiscalizacaoOperationGroup.Novos24hIrregulares, 4),
                (FiscalizacaoOperationGroup.Regularizacao24h, 3),
            ],
            (FiscalizacaoOperationGroup.Novos24hIrregulares, LampTypeCode.VS, 100, 1));

        var entries = FiscalizacaoKwhEntryMapper.ToEntries([line]);

        entries.Should().HaveCount(3);
        entries.Should().OnlyContain(e => e.IsIrregular24h && !e.CountsTowardRetroativo);
    }
}
