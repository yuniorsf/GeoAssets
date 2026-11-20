namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>
/// Writes one batch into NeutralOperator.Server's database, in one transaction of its own. The table's enabled user
/// triggers are disabled inside that transaction and restored before it commits — DDL is transactional, so no other
/// session ever sees them off — because they would overwrite what must be kept: the <c>ServiceOrders</c> row-version
/// trigger stamps a fresh <c>RowVersion</c> on every insert. (Foreign keys and checks are not triggers of this kind and stay
/// enforced.) Disabling a trigger takes the table's exclusive lock for the batch: the move runs in a maintenance window.
/// </summary>
internal static class TargetWriter
{
    /// <summary>Rows per <c>INSERT</c> statement.</summary>
    public const int RowsPerStatement = 100;

    /// <summary>Inserts <paramref name="rows"/> and returns how many were really written.</summary>
    public static async Task<int> InsertAsync(
        PostgresSession target, ResolvedTable resolved, IReadOnlyList<string?[]> rows, bool skipConflicts, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0) return 0;

        var table = resolved.Table;
        await target.BeginAsync(ct).ConfigureAwait(false);
        try
        {
            await target.ExecuteAsync(new SqlStatement().Append("SET LOCAL lock_timeout = '30s'"), ct).ConfigureAwait(false);
            var triggers = await target.QueryAsync(MovedRowSql.EnabledUserTriggers(table), ct).ConfigureAwait(false);
            foreach (var trigger in triggers)
            {
                await target.ExecuteAsync(
                    new SqlStatement().Append($"ALTER TABLE {table.QualifiedName} DISABLE TRIGGER {SqlStatement.Identifier(trigger[0]!)}"),
                    ct).ConfigureAwait(false);
            }

            var written = 0;
            foreach (var chunk in rows.Chunk(RowsPerStatement))
                written += await target.ExecuteAsync(MovedRowSql.Insert(resolved, chunk, skipConflicts), ct).ConfigureAwait(false);

            foreach (var trigger in triggers)
            {
                var mode = string.Equals(trigger[1], "A", StringComparison.Ordinal) ? "ENABLE ALWAYS" : "ENABLE";
                await target.ExecuteAsync(
                    new SqlStatement().Append($"ALTER TABLE {table.QualifiedName} {mode} TRIGGER {SqlStatement.Identifier(trigger[0]!)}"),
                    ct).ConfigureAwait(false);
            }

            await target.CommitAsync(ct).ConfigureAwait(false);
            return written;
        }
        catch
        {
            await target.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }
}
