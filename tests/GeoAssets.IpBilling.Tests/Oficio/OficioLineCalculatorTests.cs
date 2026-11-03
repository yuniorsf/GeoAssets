using FluentAssertions;
using GeoAssets.IpBilling.Oficio;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Oficio;

/// <summary>
/// Covers XD01-185's acceptance criteria for <see cref="OficioLineCalculator"/>: per-operation-
/// group-combination cases, the all-four-groups-present case, the negative-Agregado case, and
/// golden-master runs that explicitly separate "Agregado matches legacy exactly" (always true,
/// both policies agree) from "Retroativo is expected to differ" (the two
/// <see cref="OficioRetroativoPolicy"/> values intentionally disagree whenever groups 3/4 are
/// non-zero — see that type's doc comment for why this ticket flags, rather than resolves, which
/// one is "legacy-correct" for Ofício).
/// </summary>
public class OficioLineCalculatorTests
{
    private static OficioLine LineWith(params (OficioOperationGroup Group, int Count)[] groups)
    {
        var line = new OficioLine { ServiceOrderId = "order-1", CodigoInstalacao = "INST-001" };
        foreach (var (group, count) in groups)
        {
            line.OperationGroups.Add(new OficioOperationGroupDetail
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

        OficioLineCalculator.Recalculate(line);

        line.Agregado.Should().Be(0);
        line.Retroativo.Should().Be(0);
    }

    [Fact]
    public void Recalculate_OnlyGroup1Present_AgregadoAndRetroativoEqualGroup1()
    {
        var line = LineWith((OficioOperationGroup.Inclusao, 10));

        OficioLineCalculator.Recalculate(line);

        line.Agregado.Should().Be(10);
        line.Retroativo.Should().Be(10);
    }

    [Fact]
    public void Recalculate_OnlyGroup2Present_BothTotalsNegated()
    {
        var line = LineWith((OficioOperationGroup.Retirada, 7));

        OficioLineCalculator.Recalculate(line);

        line.Agregado.Should().Be(-7);
        line.Retroativo.Should().Be(-7);
    }

    [Fact]
    public void Recalculate_DefaultPolicy_OnlyGroup3Present_AffectsAgregadoAndRetroativo()
    {
        // Under the DEFAULT policy (SymmetricWithAgregado), groups 3/4 affect Retroativo too —
        // unlike Fiscalização, where they never do.
        var line = LineWith((OficioOperationGroup.Novos24hIrregulares, 4));

        OficioLineCalculator.Recalculate(line);

        line.Agregado.Should().Be(4);
        line.Retroativo.Should().Be(4, "the default policy is symmetric with Agregado");
    }

    [Fact]
    public void Recalculate_AllFourGroupsPresent_DefaultPolicy_AgregadoAndRetroativoMatch()
    {
        // Agregado = 10 - 8 + 4 - 3 = 3. Under SymmetricWithAgregado, Retroativo = 3 too.
        var line = LineWith(
            (OficioOperationGroup.Inclusao, 10),
            (OficioOperationGroup.Retirada, 8),
            (OficioOperationGroup.Novos24hIrregulares, 4),
            (OficioOperationGroup.Regularizacao24h, 3));

        OficioLineCalculator.Recalculate(line);

        line.Agregado.Should().Be(3);
        line.Retroativo.Should().Be(3);
    }

    [Fact]
    public void Recalculate_Group2ExceedsGroup1_AgregadoIsNegative_NotClamped()
    {
        // Agregado = 2 - 20 + 0 - 0 = -18 — must stay negative, never clamped to 0.
        var line = LineWith(
            (OficioOperationGroup.Inclusao, 2),
            (OficioOperationGroup.Retirada, 20));

        OficioLineCalculator.Recalculate(line);

        line.Agregado.Should().Be(-18);
        line.Retroativo.Should().Be(-18);
    }

    [Fact]
    public void Recalculate_MultipleRowsForSameGroupOnSameLine_SumTogether()
    {
        var line = LineWith(
            (OficioOperationGroup.Inclusao, 5),
            (OficioOperationGroup.Inclusao, 5));

        OficioLineCalculator.Recalculate(line);

        line.Agregado.Should().Be(10);
        line.Retroativo.Should().Be(10);
    }

    /// <summary>
    /// Golden-master: "Agregado matches legacy exactly" — Agregado is computed identically
    /// regardless of which <see cref="OficioRetroativoPolicy"/> is in effect, because the policy
    /// only ever governs Retroativo.
    /// </summary>
    [Fact]
    public void Recalculate_GoldenMaster_AgregadoIdentical_RegardlessOfRetroativoPolicy()
    {
        var lineUnderDefault = LineWith(
            (OficioOperationGroup.Inclusao, 50),
            (OficioOperationGroup.Retirada, 30),
            (OficioOperationGroup.Novos24hIrregulares, 1000),
            (OficioOperationGroup.Regularizacao24h, 999));
        var lineUnderAsymmetric = LineWith(
            (OficioOperationGroup.Inclusao, 50),
            (OficioOperationGroup.Retirada, 30),
            (OficioOperationGroup.Novos24hIrregulares, 1000),
            (OficioOperationGroup.Regularizacao24h, 999));

        OficioLineCalculator.Recalculate(lineUnderDefault, OficioRetroativoPolicy.SymmetricWithAgregado);
        OficioLineCalculator.Recalculate(lineUnderAsymmetric, OficioRetroativoPolicy.ExcludeGroups3And4);

        lineUnderDefault.Agregado.Should().Be(50 - 30 + 1000 - 999);
        lineUnderAsymmetric.Agregado.Should().Be(lineUnderDefault.Agregado, "the policy never changes Agregado, only Retroativo");
    }

    /// <summary>
    /// Golden-master: "Retroativo is expected to differ" — the two policies intentionally
    /// disagree on Retroativo whenever groups 3/4 are non-zero and don't net to zero, by design
    /// (this is the open business-rule question <see cref="OficioRetroativoPolicy"/> flags, not
    /// a bug to reconcile).
    /// </summary>
    [Fact]
    public void Recalculate_GoldenMaster_RetroativoDiffersBetweenPolicies_WhenGroups3And4Nonzero()
    {
        var lineUnderDefault = LineWith(
            (OficioOperationGroup.Inclusao, 50),
            (OficioOperationGroup.Retirada, 30),
            (OficioOperationGroup.Novos24hIrregulares, 1000),
            (OficioOperationGroup.Regularizacao24h, 999));
        var lineUnderAsymmetric = LineWith(
            (OficioOperationGroup.Inclusao, 50),
            (OficioOperationGroup.Retirada, 30),
            (OficioOperationGroup.Novos24hIrregulares, 1000),
            (OficioOperationGroup.Regularizacao24h, 999));

        OficioLineCalculator.Recalculate(lineUnderDefault, OficioRetroativoPolicy.SymmetricWithAgregado);
        OficioLineCalculator.Recalculate(lineUnderAsymmetric, OficioRetroativoPolicy.ExcludeGroups3And4);

        lineUnderDefault.Retroativo.Should().Be(50 - 30 + 1000 - 999); // 21
        lineUnderAsymmetric.Retroativo.Should().Be(50 - 30); // 20
        lineUnderDefault.Retroativo.Should().NotBe(lineUnderAsymmetric.Retroativo,
            "the two policies must genuinely disagree here — this is the flagged ambiguity, not a bug");
    }

    [Fact]
    public void Recalculate_NoPolicyPassed_UsesDefaultRetroativoPolicyConstant()
    {
        // Pins the current DefaultRetroativoPolicy (SymmetricWithAgregado) — if that constant
        // is ever flipped per the business-owner checkpoint in OficioRetroativoPolicy's doc
        // comment, this expected value (15, not the ExcludeGroups3And4 value of 10) must be
        // updated deliberately, not silently pass either way.
        var line = LineWith(
            (OficioOperationGroup.Inclusao, 10),
            (OficioOperationGroup.Novos24hIrregulares, 5));

        OficioLineCalculator.Recalculate(line);

        line.Retroativo.Should().Be(15);
    }
}
