namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>
/// Classifies — and, on <c>copy</c>, copies — a table whose rows are matched by primary key: page by page over the
/// kernel's moved rows (keyset pagination, <c>--batch-size</c> rows), the target's rows with the same keys are read and
/// each source row is identical, missing or different. A copy inserts the missing ones with
/// <c>ON CONFLICT DO NOTHING</c> in one transaction per page; a row that still did not land collides with another of the
/// target's rows on a unique index and is reported as different. Nothing is ever overwritten.
/// </summary>
internal static class KeyedTableMover
{
    public static async Task<TableOutcome> RunAsync(
        PostgresSession kernel, PostgresSession target, ResolvedTable resolved, NeutralOperatorMoveScope scope, int batchSize, bool copy, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(resolved);

        var table = resolved.Table;
        var outcome = new TableOutcome(table.DisplayName);
        var offset = MovedRowSql.Offset(table);
        var compared = table.ComparedColumns.Select(table.IndexOf).ToArray();
        var keys = table.KeyColumns.Select(table.IndexOf).ToArray();

        IReadOnlyList<string?>? after = null;
        while (true)
        {
            var page = await kernel.QueryAsync(MovedRowSql.SourcePage(resolved, scope, after, batchSize), ct).ConfigureAwait(false);
            if (page.Count == 0) break;

            after = MovedRowSql.PageKey(table, page[^1]);
            var rows = page.Select(r => r[offset..]).ToList();
            var present = await TargetRowsAsync(target, resolved, rows, keys, ct).ConfigureAwait(false);

            var missing = new List<string?[]>();
            foreach (var row in rows)
            {
                outcome.SourceRows++;
                if (!present.TryGetValue(RowValues.Key(row, keys), out var held))
                {
                    outcome.Missing++;
                    missing.Add(row);
                }
                else if (RowValues.Same(row, held, compared))
                {
                    outcome.Identical++;
                }
                else
                {
                    outcome.AddDifferent(1, $"{RowValues.Display(row, keys)} ({RowValues.DifferingColumns(table, row, held, compared)})");
                }
            }

            missing = await WithoutCollisionsAsync(target, resolved, missing, keys, outcome, ct).ConfigureAwait(false);
            if (copy && missing.Count > 0)
                await CopyAsync(target, resolved, missing, keys, compared, outcome, ct).ConfigureAwait(false);
        }

        return outcome;
    }

    /// <summary>
    /// <paramref name="missing"/> without the rows that would collide with another target row on a unique index — each
    /// counted as different rather than missing, so <c>plan</c> already shows what <c>copy</c> could not insert.
    /// </summary>
    private static async Task<List<string?[]>> WithoutCollisionsAsync(
        PostgresSession target, ResolvedTable resolved, List<string?[]> missing, int[] keys, TableOutcome outcome, CancellationToken ct)
    {
        if (missing.Count == 0 || resolved.Table.UniqueIndexes.Count == 0) return missing;

        var colliding = new HashSet<string>(StringComparer.Ordinal);
        foreach (var index in resolved.Table.UniqueIndexes)
        {
            var found = await target.QueryAsync(MovedRowSql.UniqueCollisions(resolved, index, missing), ct).ConfigureAwait(false);
            foreach (var key in found)
            {
                var joined = RowValues.Key(key);
                if (colliding.Add(joined))
                {
                    outcome.Missing--;
                    outcome.AddDifferent(1, $"{string.Join("/", key)} (another target row holds its {string.Join(", ", index.Columns)})");
                }
            }
        }

        return colliding.Count == 0 ? missing : [.. missing.Where(row => !colliding.Contains(RowValues.Key(row, keys)))];
    }

    private static async Task CopyAsync(
        PostgresSession target, ResolvedTable resolved, List<string?[]> missing, int[] keys, int[] compared, TableOutcome outcome, CancellationToken ct)
    {
        var written = await TargetWriter.InsertAsync(target, resolved, missing, skipConflicts: true, ct).ConfigureAwait(false);
        outcome.Copied += written;
        if (written == missing.Count) return;

        var landed = await TargetRowsAsync(target, resolved, missing, keys, ct).ConfigureAwait(false);
        foreach (var row in missing)
        {
            if (!landed.TryGetValue(RowValues.Key(row, keys), out var held))
                outcome.AddDifferent(1, $"{RowValues.Display(row, keys)} (collides with another row of the target on a unique index)");
            else if (!RowValues.Same(row, held, compared))
                outcome.AddDifferent(1, $"{RowValues.Display(row, keys)} (written concurrently with other content)");
        }
    }

    private static async Task<Dictionary<string, string?[]>> TargetRowsAsync(
        PostgresSession target, ResolvedTable resolved, List<string?[]> rows, int[] keys, CancellationToken ct)
    {
        var keyValues = rows.Select(r => (IReadOnlyList<string?>)[.. keys.Select(i => r[i])]).ToList();
        var held = await target.QueryAsync(MovedRowSql.TargetRows(resolved, keyValues), ct).ConfigureAwait(false);
        return held.ToDictionary(r => RowValues.Key(r, keys), StringComparer.Ordinal);
    }
}
