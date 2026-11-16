using FluentAssertions;
using GeoAssets.IpBilling.Calculation;
using GeoAssets.IpBilling.Oficio;
using GeoAssets.IpBilling.Models;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Oficio;

/// <summary>Covers every guard/branch of <see cref="OficioKwhEntryMapper"/> (XD01-301, IDs MAP-*).</summary>
public class OficioKwhEntryMapperTests
{
    private static OficioLine Line(
        (OficioOperationGroup Group, int Count)[] groups,
        params (OficioOperationGroup Group, LampTypeCode Code, int WattageW, int Quantity)[] lamps)
    {
        var line = new OficioLine { ServiceOrderId = "order-1", CodigoInstalacao = "INST-001" };
        line.OperationGroups.AddRange(groups.Select(g => new OficioOperationGroupDetail
        {
            LineId = line.Id,
            OperationGroup = g.Group,
            Count = g.Count,
        }));
        line.LampDetails.AddRange(lamps.Select(l => new OficioLampDetail
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
        var act = () => OficioKwhEntryMapper.ToEntries(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void CountWithoutLampDetail_MAP_G1_NullLine_Throws()
    {
        var act = () => OficioKwhEntryMapper.CountWithoutLampDetail(null!, OficioOperationGroup.Inclusao);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ToEntries_MAP_G2_LampQuantityExceedsGroupCount_ThrowsNamingCodigoAndGroup()
    {
        var line = Line(
            [(OficioOperationGroup.Inclusao, 5)],
            (OficioOperationGroup.Inclusao, LampTypeCode.VS, 100, 6));

        var act = () => OficioKwhEntryMapper.ToEntries([line]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*INST-001*").WithMessage("*Inclusao*");
    }

    [Fact]
    public void ToEntries_MAP_B1_NoLampDetails_OneLamplessEntryPerNonEmptyGroup_WithSignAnd24hFlag()
    {
        var line = Line(
        [
            (OficioOperationGroup.Inclusao, 10),
            (OficioOperationGroup.Retirada, 8),
            (OficioOperationGroup.Novos24hIrregulares, 4),
            (OficioOperationGroup.Regularizacao24h, 3),
        ]);

        var entries = OficioKwhEntryMapper.ToEntries([line]);

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
            [(OficioOperationGroup.Inclusao, 10)],
            (OficioOperationGroup.Inclusao, LampTypeCode.VS, 100, 6),
            (OficioOperationGroup.Inclusao, LampTypeCode.LD, 60, 4));

        var entries = OficioKwhEntryMapper.ToEntries([line]);

        entries.Should().Equal(
            new InspectionPointEntry(6, 1, false, true, LampTypeCode.VS, 100),
            new InspectionPointEntry(4, 1, false, true, LampTypeCode.LD, 60));
        OficioKwhEntryMapper.CountWithoutLampDetail(line, OficioOperationGroup.Inclusao).Should().Be(0);
    }

    [Fact]
    public void ToEntries_MAP_B3_PartialCover_ExactEntriesPlusOneRemainderEntry()
    {
        var line = Line(
            [(OficioOperationGroup.Retirada, 10)],
            (OficioOperationGroup.Retirada, LampTypeCode.VS, 100, 6));

        var entries = OficioKwhEntryMapper.ToEntries([line]);

        entries.Should().Equal(
            new InspectionPointEntry(6, -1, false, true, LampTypeCode.VS, 100),
            new InspectionPointEntry(4, -1, false, true));
        OficioKwhEntryMapper.CountWithoutLampDetail(line, OficioOperationGroup.Retirada).Should().Be(4);
    }

    [Fact]
    public void ToEntries_MAP_B4_EmptyOrUnspecifiedGroup_ProducesNoEntry()
    {
        var line = Line(
        [
            (OficioOperationGroup.Unspecified, 7),
            (OficioOperationGroup.Inclusao, 0),
        ]);

        OficioKwhEntryMapper.ToEntries([line]).Should().BeEmpty();
        OficioKwhEntryMapper.ToEntries([]).Should().BeEmpty();
    }

    private static OficioLine Groups3And4Line() => Line(
        [
            (OficioOperationGroup.Inclusao, 2),
            (OficioOperationGroup.Novos24hIrregulares, 4),
            (OficioOperationGroup.Regularizacao24h, 3),
        ],
        (OficioOperationGroup.Novos24hIrregulares, LampTypeCode.VS, 100, 1));

    [Fact]
    public void ToEntries_MAP_B6_DefaultPolicy_Groups3And4AreRetroactive()
    {
        var entries = OficioKwhEntryMapper.ToEntries([Groups3And4Line()]);

        entries.Should().HaveCount(4);
        entries.Should().OnlyContain(e => e.CountsTowardRetroativo);
        entries.Skip(1).Should().OnlyContain(e => e.IsIrregular24h);
    }

    [Fact]
    public void ToEntries_MAP_B6_SymmetricWithAgregado_Groups3And4AreRetroactive()
    {
        var entries = OficioKwhEntryMapper.ToEntries([Groups3And4Line()], OficioRetroativoPolicy.SymmetricWithAgregado);

        entries.Should().OnlyContain(e => e.CountsTowardRetroativo);
    }

    [Fact]
    public void ToEntries_MAP_B6_ExcludeGroups3And4_OnlyGroups1And2AreRetroactive()
    {
        var entries = OficioKwhEntryMapper.ToEntries([Groups3And4Line()], OficioRetroativoPolicy.ExcludeGroups3And4);

        entries.Where(e => !e.IsIrregular24h).Should().ContainSingle().Which.CountsTowardRetroativo.Should().BeTrue();
        entries.Where(e => e.IsIrregular24h).Should().HaveCount(3).And.OnlyContain(e => !e.CountsTowardRetroativo);
    }
}
