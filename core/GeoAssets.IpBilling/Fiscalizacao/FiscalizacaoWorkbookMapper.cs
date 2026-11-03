namespace GeoAssets.IpBilling.Fiscalizacao;

/// <summary>
/// Groups flat <see cref="FiscalizacaoWorkbookRow"/>s (one per operation-group per installation)
/// into the <see cref="FiscalizacaoLine"/>/<see cref="FiscalizacaoOperationGroupDetail"/> graph
/// <see cref="FiscalizacaoCalculationService"/> operates on. Pure/no I/O — mirrors
/// <c>Levantamento.LevantamentoWorkbookMapper</c>'s "grouping only, math happens afterward"
/// separation: <see cref="FiscalizacaoLineCalculator.Recalculate"/> is a distinct step.
/// </summary>
public static class FiscalizacaoWorkbookMapper
{
    public static List<FiscalizacaoLine> ToLines(
        string serviceOrderId, IReadOnlyList<FiscalizacaoWorkbookRow> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceOrderId);
        ArgumentNullException.ThrowIfNull(rows);

        return rows
            .GroupBy(r => r.CodigoInstalacao, StringComparer.Ordinal)
            .Select(group =>
            {
                var line = new FiscalizacaoLine
                {
                    ServiceOrderId = serviceOrderId,
                    CodigoInstalacao = group.Key,
                };

                foreach (var row in group)
                {
                    line.OperationGroups.Add(new FiscalizacaoOperationGroupDetail
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
