namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// Pure business rule for one <see cref="OficioLine"/>'s <see cref="OficioLine.Agregado"/>/
/// <see cref="OficioLine.Retroativo"/> totals from its per-operation-group counts (XD01-185's
/// own description). Isolated from <see cref="OficioCalculationService"/>, mirroring
/// <c>Fiscalizacao.FiscalizacaoLineCalculator</c>'s separation.
/// </summary>
public static class OficioLineCalculator
{
    /// <summary>
    /// The default <see cref="OficioRetroativoPolicy"/> applied when a caller does not pass one
    /// explicitly to <see cref="Recalculate"/>. See <see cref="OficioRetroativoPolicy"/>'s doc
    /// comment for the open business-rule question this default is a placeholder for — flip
    /// this one constant if the business owner confirms the asymmetric (Fiscalização-style)
    /// rule should apply here too.
    /// </summary>
    public const OficioRetroativoPolicy DefaultRetroativoPolicy = OficioRetroativoPolicy.SymmetricWithAgregado;

    /// <summary>
    /// Recomputes and stores <see cref="OficioLine.Agregado"/>/<see cref="OficioLine.Retroativo"/>
    /// from <paramref name="line"/>'s <see cref="OficioLine.OperationGroups"/>. A group with no
    /// rows contributes <c>0</c>; multiple rows for the same group on the same line sum together.
    ///
    /// <c>Agregado = Agregado₁ − Agregado₂ + Agregado₃ − Agregado₄</c> always — can legitimately
    /// be negative, never clamped.
    ///
    /// <c>Retroativo</c> follows <paramref name="policy"/> (defaulting to
    /// <see cref="DefaultRetroativoPolicy"/>): either the same four-term formula as
    /// <see cref="OficioLine.Agregado"/>, or Fiscalização's groups-1/2-only formula.
    /// </summary>
    public static void Recalculate(OficioLine line, OficioRetroativoPolicy? policy = null)
    {
        ArgumentNullException.ThrowIfNull(line);

        var effectivePolicy = policy ?? DefaultRetroativoPolicy;

        var g1 = CountFor(line, OficioOperationGroup.Inclusao);
        var g2 = CountFor(line, OficioOperationGroup.Retirada);
        var g3 = CountFor(line, OficioOperationGroup.Novos24hIrregulares);
        var g4 = CountFor(line, OficioOperationGroup.Regularizacao24h);

        line.Agregado = g1 - g2 + g3 - g4;
        line.Retroativo = effectivePolicy == OficioRetroativoPolicy.ExcludeGroups3And4
            ? g1 - g2
            : g1 - g2 + g3 - g4;
    }

    private static int CountFor(OficioLine line, OficioOperationGroup group) =>
        line.OperationGroups
            .Where(d => d.OperationGroup == group)
            .Sum(d => d.Count);
}
