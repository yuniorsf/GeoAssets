namespace GeoAssets.IpBilling.Oficio;

/// <summary>
/// Groups flat <see cref="OficioWorkbookRow"/>s (one per operation-group per installation) into
/// the <see cref="OficioLine"/>/<see cref="OficioOperationGroupDetail"/> graph
/// <see cref="OficioCalculationService"/> operates on. Pure/no I/O — mirrors
/// <c>Fiscalizacao.FiscalizacaoWorkbookMapper</c>'s "grouping only, math happens afterward"
/// separation: <see cref="OficioLineCalculator.Recalculate"/> is a distinct step.
/// </summary>
public static class OficioWorkbookMapper
{
    public static List<OficioLine> ToLines(
        string serviceOrderId, IReadOnlyList<OficioWorkbookRow> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceOrderId);
        ArgumentNullException.ThrowIfNull(rows);

        return rows
            .GroupBy(r => r.CodigoInstalacao, StringComparer.Ordinal)
            .Select(group =>
            {
                var line = new OficioLine
                {
                    ServiceOrderId = serviceOrderId,
                    CodigoInstalacao = group.Key,
                };

                foreach (var row in group)
                {
                    line.OperationGroups.Add(new OficioOperationGroupDetail
                    {
                        LineId = line.Id,
                        OperationGroup = row.OperationGroup,
                        Count = row.Count,
                    });
                }

                return line;
            })
            .ToList();
    }
}
