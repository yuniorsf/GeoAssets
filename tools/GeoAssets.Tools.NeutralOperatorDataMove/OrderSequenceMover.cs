namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>
/// Classifies — and, on <c>copy</c>, copies — a moved order's history rows (<see cref="MovedRowScope.OrderSequence"/>):
/// for each page of <c>--batch-size</c> moved orders, the kernel's and the target's rows of those orders are read in
/// identity order and compared order by order as sequences of their non-identity columns. An order the target holds no
/// row of is missing (all its rows are inserted, in the kernel's order, in one transaction per page, the target
/// numbering them); the same sequence is identical; anything else is different and never touched.
/// </summary>
internal static class OrderSequenceMover
{
    public static async Task<TableOutcome> RunAsync(
        PostgresSession kernel, PostgresSession target, ResolvedTable resolved, NeutralOperatorMoveScope scope, int batchSize, bool copy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(resolved);

        var table = resolved.Table;
        var outcome = new TableOutcome(table.DisplayName);
        var order = table.IndexOf(MovedTableCatalog.ServiceOrderIdColumn);
        var compared = table.ComparedColumns.Select(table.IndexOf).ToArray();

        string? after = null;
        while (true)
        {
            var orders = (await kernel.QueryAsync(MovedRowSql.MovedOrderPage(scope, after, batchSize), ct).ConfigureAwait(false))
                .Select(r => r[0]!)
                .ToList();
            if (orders.Count == 0) break;

            after = orders[^1];
            var source = ByOrder(await kernel.QueryAsync(MovedRowSql.RowsOfOrders(resolved, orders), ct).ConfigureAwait(false), order);
            var present = ByOrder(await target.QueryAsync(MovedRowSql.RowsOfOrders(resolved, orders), ct).ConfigureAwait(false), order);

            var missing = new List<string?[]>();
            foreach (var orderId in orders)
            {
                var rows = source.GetValueOrDefault(orderId) ?? [];
                var held = present.GetValueOrDefault(orderId) ?? [];
                outcome.SourceRows += rows.Count;
                if (held.Count == 0)
                {
                    outcome.Missing += rows.Count;
                    missing.AddRange(rows);
                }
                else if (SameSequence(rows, held, compared))
                {
                    outcome.Identical += rows.Count;
                }
                else
                {
                    outcome.AddDifferent(
                        Math.Max(rows.Count, held.Count),
                        string.Create(System.Globalization.CultureInfo.InvariantCulture, $"order {orderId} ({rows.Count} row(s) in the kernel, {held.Count} in the target, not the same)"));
                }
            }

            if (copy && missing.Count > 0)
                outcome.Copied += await TargetWriter.InsertAsync(target, resolved, missing, skipConflicts: false, ct).ConfigureAwait(false);
        }

        return outcome;
    }

    private static Dictionary<string, List<string?[]>> ByOrder(List<string?[]> rows, int order)
    {
        var byOrder = new Dictionary<string, List<string?[]>>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var key = row[order] ?? string.Empty;
            if (!byOrder.TryGetValue(key, out var list))
            {
                list = [];
                byOrder[key] = list;
            }

            list.Add(row);
        }

        return byOrder;
    }

    private static bool SameSequence(List<string?[]> left, List<string?[]> right, int[] compared)
    {
        if (left.Count != right.Count) return false;

        for (var i = 0; i < left.Count; i++)
        {
            if (!RowValues.Same(left[i], right[i], compared)) return false;
        }

        return true;
    }
}
