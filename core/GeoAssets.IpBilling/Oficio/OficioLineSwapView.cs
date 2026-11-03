namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// Pure derived/query-time view over a set of <see cref="OficioLine"/>s — no new persisted
/// field, additive only, per this ticket's own description (XD01-185): never changes
/// <see cref="OficioLine.Agregado"/>/<see cref="OficioLine.Retroativo"/>, which keep the
/// legacy's emergent per-line signed-netting calculation exactly as computed by
/// <see cref="OficioLineCalculator"/>.
///
/// A line is flagged as a swap when it carries both an inclusão (group 1) and a retirada
/// (group 2) operation — either on the <em>same</em> persisted <see cref="OficioLine"/> (the
/// normal case, since <see cref="OficioWorkbookMapper"/> groups upload rows by
/// <see cref="OficioLine.CodigoInstalacao"/>), or <em>across</em> two separate
/// <see cref="OficioLine"/> records that happen to share the same
/// <see cref="OficioLine.CodigoInstalacao"/> (a defensive cross-line case for callers that don't
/// go through the normal grouping mapper).
/// </summary>
public static class OficioLineSwapView
{
    public static IReadOnlyList<OficioLineSwapResult> Compute(IReadOnlyList<OficioLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var results = new List<OficioLineSwapResult>();

        foreach (var group in lines.GroupBy(l => l.CodigoInstalacao, StringComparer.Ordinal))
        {
            var crossLineSwap = group.Count() > 1
                && group.Any(HasInclusao)
                && group.Any(HasRetirada);

            foreach (var line in group)
            {
                var isSwap = crossLineSwap || (HasInclusao(line) && HasRetirada(line));
                results.Add(new OficioLineSwapResult(line.Id, line.CodigoInstalacao, isSwap));
            }
        }

        return results;
    }

    private static bool HasInclusao(OficioLine line) =>
        line.OperationGroups.Any(d => d.OperationGroup == OficioOperationGroup.Inclusao);

    private static bool HasRetirada(OficioLine line) =>
        line.OperationGroups.Any(d => d.OperationGroup == OficioOperationGroup.Retirada);
}
