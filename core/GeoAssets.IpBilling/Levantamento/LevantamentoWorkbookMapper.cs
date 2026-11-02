namespace GeoAssets.IpBilling.Levantamento;

/// <summary>
/// Groups flat <see cref="LevantamentoWorkbookRow"/>s (one per lamp-type/wattage per
/// transformer) into the <see cref="LevantamentoTransformerLine"/>/<see cref="LevantamentoLampDetail"/>
/// graph <see cref="LevantamentoCalculationService"/> operates on. Pure/no I/O — transformer
/// resolution, new-vs-existing detection, and kWh math all happen afterward, not here.
/// </summary>
public static class LevantamentoWorkbookMapper
{
    public static List<LevantamentoTransformerLine> ToTransformerLines(
        string serviceOrderId, IReadOnlyList<LevantamentoWorkbookRow> rows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceOrderId);
        ArgumentNullException.ThrowIfNull(rows);

        var byCodigoInstalacao = rows
            .GroupBy(r => r.CodigoInstalacao, StringComparer.Ordinal)
            .Select(group =>
            {
                var line = new LevantamentoTransformerLine
                {
                    ServiceOrderId = serviceOrderId,
                    CodigoInstalacao = group.Key,
                };

                foreach (var row in group)
                {
                    line.LampDetails.Add(new LevantamentoLampDetail
                    {
                        TransformerLineId = line.Id,
                        Code = row.LampTypeCode,
                        WattageW = row.WattageW,
                        Quantity = row.Quantity,
                    });
                }

                return line;
            })
            .ToList();

        return byCodigoInstalacao;
    }
}
