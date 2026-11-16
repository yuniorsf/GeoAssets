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

                    AddLampDetail(line, row);
                }

                return line;
            })
            .ToList();
    }

    /// <summary>
    /// The inverse of <see cref="ToLines"/>, for recalculating from persisted lines (XD01-301).
    /// A line without lamp details yields one row per <see cref="FiscalizacaoOperationGroupDetail"/>,
    /// exactly as before lamp detail existed. A line with lamp details yields, per operation
    /// group, one row per lamp detail plus one lamp-less row for whatever part of the group's
    /// count the lamp details do not cover — so re-mapping reproduces the same counts and the
    /// same lamp details.
    /// </summary>
    /// <exception cref="InvalidOperationException">A line's lamp details for an operation group
    /// add up to more points than the group's own count.</exception>
    public static List<FiscalizacaoWorkbookRow> ToRows(IReadOnlyList<FiscalizacaoLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var rows = new List<FiscalizacaoWorkbookRow>();
        foreach (var line in lines)
        {
            if (line.LampDetails.Count == 0)
            {
                rows.AddRange(line.OperationGroups.Select(detail =>
                    new FiscalizacaoWorkbookRow(line.CodigoInstalacao, detail.OperationGroup, detail.Count)));
                continue;
            }

            foreach (var group in line.OperationGroups.Select(d => d.OperationGroup).Distinct())
            {
                var lampDetails = line.LampDetails.Where(l => l.OperationGroup == group).ToList();
                rows.AddRange(lampDetails.Select(lamp =>
                    new FiscalizacaoWorkbookRow(line.CodigoInstalacao, group, lamp.Quantity, lamp.Code, lamp.WattageW)));

                var remainder = FiscalizacaoKwhEntryMapper.CountWithoutLampDetail(line, group);
                if (remainder > 0)
                {
                    rows.Add(new FiscalizacaoWorkbookRow(line.CodigoInstalacao, group, remainder));
                }
            }
        }

        return rows;
    }

    private static void AddLampDetail(FiscalizacaoLine line, FiscalizacaoWorkbookRow row)
    {
        if (row.LampCode is null != row.WattageW is null)
        {
            throw new ArgumentException(
                $"Row for '{row.CodigoInstalacao}' must carry both lamp type and wattage, or neither.", nameof(row));
        }

        if (row.LampCode is not { } code || row.WattageW is not { } wattageW)
        {
            return;
        }

        line.LampDetails.Add(new FiscalizacaoLampDetail
        {
            LineId = line.Id,
            OperationGroup = row.OperationGroup,
            Code = code,
            WattageW = wattageW,
            Quantity = row.Count,
        });
    }
}
