using FluentAssertions;
using GeoAssets.IpBilling.Fiscalizacao;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Fiscalizacao;

/// <summary>
/// Covers XD01-182's acceptance criteria: per-operation-group-combination cases, the
/// all-four-groups-present case, the negative-Agregado case, and a golden-master test proving
/// the ops-3/4 Retroativo-exclusion is preserved (not accidentally "fixed" to be symmetric with
/// Agregado — contrast with Ofício's XD01-185, resolved differently and out of scope here).
/// </summary>
public class FiscalizacaoLineCalculatorTests
{
    private static FiscalizacaoLine LineWith(params (FiscalizacaoOperationGroup Group, int Count)[] groups)
    {
        var line = new FiscalizacaoLine { ServiceOrderId = "order-1", CodigoInstalacao = "INST-001" };
        foreach (var (group, count) in groups)
        {
            line.OperationGroups.Add(new FiscalizacaoOperationGroupDetail
            {
                LineId = line.Id,
                OperationGroup = group,
                Count = count,
            });
        }

        return line;
    }

    [Fact]
    public void Recalculate_NoGroupsPresent_BothTotalsZero()
    {
        var line = LineWith();

        FiscalizacaoLineCalculator.Recalculate(line);

        line.Agregado.Should().Be(0);
        line.Retroativo.Should().Be(0);
    }

    [Fact]
    public void Recalculate_OnlyGroup1Present_AgregadoAndRetroativoEqualGroup1()
    {
        var line = LineWith((FiscalizacaoOperationGroup.Fiscalizacao, 10));

        FiscalizacaoLineCalculator.Recalculate(line);

        line.Agregado.Should().Be(10);
        line.Retroativo.Should().Be(10);
    }

    [Fact]
    public void Recalculate_OnlyGroup2Present_BothTotalsNegated()
    {
        var line = LineWith((FiscalizacaoOperationGroup.UltimoLevantamento, 7));

        FiscalizacaoLineCalculator.Recalculate(line);

        line.Agregado.Should().Be(-7);
        line.Retroativo.Should().Be(-7);
    }

    [Fact]
    public void Recalculate_OnlyGroup3Present_AffectsAgregadoOnly()
    {
        var line = LineWith((FiscalizacaoOperationGroup.Novos24hIrregulares, 4));

        FiscalizacaoLineCalculator.Recalculate(line);

        line.Agregado.Should().Be(4);
        line.Retroativo.Should().Be(0);
    }

    [Fact]
    public void Recalculate_OnlyGroup4Present_AffectsAgregadoOnlyAndNegated()
    {
        var line = LineWith((FiscalizacaoOperationGroup.Regularizacao24h, 3));

        FiscalizacaoLineCalculator.Recalculate(line);

        line.Agregado.Should().Be(-3);
        line.Retroativo.Should().Be(0);
    }

    [Fact]
    public void Recalculate_AllFourGroupsPresent_AgregadoCombinesAllFourSigned()
    {
        // Agregado = 10 - 8 + 4 - 3 = 3; Retroativo = 10 - 8 = 2 (groups 3/4 excluded).
        var line = LineWith(
            (FiscalizacaoOperationGroup.Fiscalizacao, 10),
            (FiscalizacaoOperationGroup.UltimoLevantamento, 8),
            (FiscalizacaoOperationGroup.Novos24hIrregulares, 4),
            (FiscalizacaoOperationGroup.Regularizacao24h, 3));

        FiscalizacaoLineCalculator.Recalculate(line);

        line.Agregado.Should().Be(3);
        line.Retroativo.Should().Be(2);
    }

    [Fact]
    public void Recalculate_Group2ExceedsGroup1_AgregadoIsNegative_NotClamped()
    {
        // Agregado = 2 - 20 + 0 - 0 = -18 — must stay negative, never clamped to 0.
        var line = LineWith(
            (FiscalizacaoOperationGroup.Fiscalizacao, 2),
            (FiscalizacaoOperationGroup.UltimoLevantamento, 20));

        FiscalizacaoLineCalculator.Recalculate(line);

        line.Agregado.Should().Be(-18);
        line.Retroativo.Should().Be(-18);
    }

    /// <summary>
    /// Golden-master: groups 3/4 move Agregado but must NEVER leak into Retroativo, even when
    /// they are large enough that a "fixed"/symmetric formula would produce a visibly different
    /// (and wrong, per this ticket's explicit instruction) Retroativo value.
    /// </summary>
    [Fact]
    public void Recalculate_GoldenMaster_Groups3And4ExcludedFromRetroativo()
    {
        var line = LineWith(
            (FiscalizacaoOperationGroup.Fiscalizacao, 50),
            (FiscalizacaoOperationGroup.UltimoLevantamento, 30),
            (FiscalizacaoOperationGroup.Novos24hIrregulares, 1000),
            (FiscalizacaoOperationGroup.Regularizacao24h, 1000));

        FiscalizacaoLineCalculator.Recalculate(line);

        // Agregado = 50 - 30 + 1000 - 1000 = 20.
        line.Agregado.Should().Be(20);
        // Retroativo = 50 - 30 = 20 ONLY — the symmetric/"fixed" formula would also give 20 here
        // by coincidence, so assert the exclusion directly: swapping groups 3/4's huge values
        // must not change Retroativo at all.
        line.Retroativo.Should().Be(20);

        var withDifferentGroups3And4 = LineWith(
            (FiscalizacaoOperationGroup.Fiscalizacao, 50),
            (FiscalizacaoOperationGroup.UltimoLevantamento, 30),
            (FiscalizacaoOperationGroup.Novos24hIrregulares, 1),
            (FiscalizacaoOperationGroup.Regularizacao24h, 999));

        FiscalizacaoLineCalculator.Recalculate(withDifferentGroups3And4);

        withDifferentGroups3And4.Agregado.Should().Be(50 - 30 + 1 - 999);
        withDifferentGroups3And4.Retroativo.Should().Be(20, "groups 3/4 must never affect Retroativo regardless of their values");
    }

    [Fact]
    public void Recalculate_MultipleRowsForSameGroupOnSameLine_SumTogether()
    {
        var line = LineWith(
            (FiscalizacaoOperationGroup.Fiscalizacao, 5),
            (FiscalizacaoOperationGroup.Fiscalizacao, 5));

        FiscalizacaoLineCalculator.Recalculate(line);

        line.Agregado.Should().Be(10);
        line.Retroativo.Should().Be(10);
    }
}
