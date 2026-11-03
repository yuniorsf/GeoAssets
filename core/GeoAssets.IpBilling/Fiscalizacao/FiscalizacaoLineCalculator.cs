namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// Pure business rule for one <see cref="FiscalizacaoLine"/>'s <see cref="FiscalizacaoLine.Agregado"/>/
/// <see cref="FiscalizacaoLine.Retroativo"/> totals from its per-operation-group counts
/// (XD01-182's own description). Isolated from <see cref="FiscalizacaoCalculationService"/> so
/// the two deliberately-asymmetric formulas below are directly unit-testable without any
/// repository/order plumbing.
/// </summary>
public static class FiscalizacaoLineCalculator
{
    /// <summary>
    /// Recomputes and stores <see cref="FiscalizacaoLine.Agregado"/>/<see cref="FiscalizacaoLine.Retroativo"/>
    /// from <paramref name="line"/>'s <see cref="FiscalizacaoLine.OperationGroups"/>. A group with
    /// no rows contributes <c>0</c>; multiple rows for the same group on the same line sum together.
    ///
    /// <c>Agregado = Agregado₁ − Agregado₂ + Agregado₃ − Agregado₄</c> — can legitimately be
    /// negative, never clamped.
    ///
    /// <c>Retroativo = Retroativo₁ − Retroativo₂</c> ONLY. Operation groups 3/4
    /// (<see cref="FiscalizacaoOperationGroup.Novos24hIrregulares"/>/<see cref="FiscalizacaoOperationGroup.Regularizacao24h"/>)
    /// are deliberately excluded here — a confirmed real business rule, not a gap to "fix" by
    /// making it symmetric with <see cref="FiscalizacaoLine.Agregado"/>.
    /// </summary>
    public static void Recalculate(FiscalizacaoLine line)
    {
        ArgumentNullException.ThrowIfNull(line);

        var g1 = CountFor(line, FiscalizacaoOperationGroup.Fiscalizacao);
        var g2 = CountFor(line, FiscalizacaoOperationGroup.UltimoLevantamento);
        var g3 = CountFor(line, FiscalizacaoOperationGroup.Novos24hIrregulares);
        var g4 = CountFor(line, FiscalizacaoOperationGroup.Regularizacao24h);

        line.Agregado = g1 - g2 + g3 - g4;
        line.Retroativo = g1 - g2;
    }

    private static int CountFor(FiscalizacaoLine line, FiscalizacaoOperationGroup group) =>
        line.OperationGroups
            .Where(d => d.OperationGroup == group)
            .Sum(d => d.Count);
}
