using FluentAssertions;
using GeoAssets.IpBilling.Oficio;
using Xunit;

namespace GeoAssets.IpBilling.Tests.Oficio;

/// <summary>
/// Covers XD01-185's acceptance criteria for <see cref="OficioLineSwapView"/>: same-line swap
/// detection (the normal case, since <see cref="OficioWorkbookMapper"/> groups upload rows by
/// installation) and cross-line swap detection (a defensive case for lines that weren't grouped
/// through that mapper). Also asserts the view never mutates Agregado/Retroativo — purely
/// additive, per this ticket's own description.
/// </summary>
public class OficioLineSwapViewTests
{
    private static OficioLine LineWith(string codigo, params OficioOperationGroup[] groups)
    {
        var line = new OficioLine { ServiceOrderId = "order-1", CodigoInstalacao = codigo };
        foreach (var group in groups)
        {
            line.OperationGroups.Add(new OficioOperationGroupDetail
            {
                LineId = line.Id,
                OperationGroup = group,
                Count = 1,
            });
        }

        return line;
    }

    [Fact]
    public void Compute_LineWithOnlyInclusao_NotFlaggedAsSwap()
    {
        var line = LineWith("INST-001", OficioOperationGroup.Inclusao);

        var result = OficioLineSwapView.Compute([line]);

        result.Should().ContainSingle(r => r.LineId == line.Id && !r.IsSwap);
    }

    [Fact]
    public void Compute_LineWithOnlyRetirada_NotFlaggedAsSwap()
    {
        var line = LineWith("INST-001", OficioOperationGroup.Retirada);

        var result = OficioLineSwapView.Compute([line]);

        result.Should().ContainSingle(r => r.LineId == line.Id && !r.IsSwap);
    }

    [Fact]
    public void Compute_SameLineHasBothInclusaoAndRetirada_FlaggedAsSwap()
    {
        var line = LineWith("INST-001", OficioOperationGroup.Inclusao, OficioOperationGroup.Retirada);

        var result = OficioLineSwapView.Compute([line]);

        result.Should().ContainSingle(r => r.LineId == line.Id && r.IsSwap);
    }

    [Fact]
    public void Compute_SameLineHasInclusaoRetiradaAndOtherGroups_StillFlaggedAsSwap()
    {
        var line = LineWith(
            "INST-001",
            OficioOperationGroup.Inclusao,
            OficioOperationGroup.Retirada,
            OficioOperationGroup.Novos24hIrregulares,
            OficioOperationGroup.Regularizacao24h);

        var result = OficioLineSwapView.Compute([line]);

        result.Should().ContainSingle(r => r.LineId == line.Id && r.IsSwap);
    }

    [Fact]
    public void Compute_CrossLine_SameCodigoSplitAcrossTwoLines_BothFlaggedAsSwap()
    {
        var inclusaoLine = LineWith("INST-001", OficioOperationGroup.Inclusao);
        var retiradaLine = LineWith("INST-001", OficioOperationGroup.Retirada);

        var result = OficioLineSwapView.Compute([inclusaoLine, retiradaLine]);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(r => r.IsSwap.Should().BeTrue());
    }

    [Fact]
    public void Compute_CrossLine_DifferentCodigos_NeitherFlaggedAsSwap()
    {
        var inclusaoLine = LineWith("INST-001", OficioOperationGroup.Inclusao);
        var retiradaLine = LineWith("INST-002", OficioOperationGroup.Retirada);

        var result = OficioLineSwapView.Compute([inclusaoLine, retiradaLine]);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(r => r.IsSwap.Should().BeFalse());
    }

    [Fact]
    public void Compute_DoesNotMutateAgregadoOrRetroativo()
    {
        var line = LineWith("INST-001", OficioOperationGroup.Inclusao, OficioOperationGroup.Retirada);
        line.Agregado = 42;
        line.Retroativo = 7;

        OficioLineSwapView.Compute([line]);

        line.Agregado.Should().Be(42, "the swap view is additive and must never touch the netting math");
        line.Retroativo.Should().Be(7);
    }
}
