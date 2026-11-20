namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>
/// <c>remove --confirm</c>: one transaction on the kernel's database that
/// <list type="number">
///   <item>locks every moved table against writers (<c>SHARE ROW EXCLUSIVE</c>, readers go on; <c>lock_timeout</c> 30 s);</item>
///   <item>re-runs the integrity checks and <c>verify</c> on what it locked, and rolls back — nothing deleted, exit 3 — when
///   any moved row is missing in the target or differs from its copy (a NeutralOperator order created or changed after the
///   copy is one of those), or anything else conflicts;</item>
///   <item>removes the moved rows children first: the vertical's schema, the copied undelivered outbox messages, the
///   orders' targets, history and the orders themselves, deepest first. The orders' targets are <b>re-keyed, not
///   deleted</b>: in the kernel's database they are the global target ledger's entries of those orders (ADR-001 D2), under
///   the in-process <c>kernel</c> vertical, and deleting their orders would cascade them away. Each moves to
///   <c>(neutral-operator, order id)</c> — same id, feature, status, version, release instant and organization; the order's
///   type and its policy's exclusivity stored as a remote vertical's entry carries them — which is the key
///   NeutralOperator.Server reserves, lists and releases its orders' targets under. Nothing else of the ledger is touched;</item>
///   <item>checks that each table lost exactly the rows it verified, and commits.</item>
/// </list>
/// </summary>
internal static class KernelRemoval
{
    public static async Task RunAsync(
        PostgresSession kernel,
        PostgresSession target,
        IReadOnlyList<ResolvedTable> tables,
        NeutralOperatorMoveScope scope,
        DataMoveOptions options,
        DataMoveReport report,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(report);

        await kernel.BeginAsync(ct).ConfigureAwait(false);
        try
        {
            await kernel.ExecuteAsync(new SqlStatement().Append("SET LOCAL lock_timeout = '30s'"), ct).ConfigureAwait(false);
            await kernel.ExecuteAsync(
                new SqlStatement().Append($"LOCK TABLE {string.Join(", ", tables.Select(t => t.Table.QualifiedName))} IN SHARE ROW EXCLUSIVE MODE"),
                ct).ConfigureAwait(false);

            await IntegrityChecks.RunAsync(kernel, target, scope, options, report, ct).ConfigureAwait(false);
            var outcomes = await DataMoveRunner.ClassifyAsync(kernel, target, tables, scope, options.BatchSize, copy: false, ct).ConfigureAwait(false);
            report.SetTables(outcomes);
            if (report.HasRefusal || report.HasConflict || report.HasMissing)
            {
                await kernel.RollbackAsync().ConfigureAwait(false);
                report.Add(DataMoveIssueSeverity.Refusal, "remove.refused",
                    "Not every moved row is in NeutralOperator.Server's database with identical content (or another check failed): nothing was deleted. Run copy, then verify.");
                return;
            }

            for (var i = tables.Count - 1; i >= 0; i--)
            {
                var outcome = outcomes[i];
                outcome.Removed = await RemoveAsync(kernel, tables[i], scope, ct).ConfigureAwait(false);
                if (outcome.Removed != outcome.SourceRows)
                {
                    await kernel.RollbackAsync().ConfigureAwait(false);
                    foreach (var rolledBack in outcomes) rolledBack.Removed = 0;
                    report.Add(DataMoveIssueSeverity.Refusal, "remove.count-mismatch",
                        $"{outcome.Table} would lose {outcome.Removed} row(s) where {outcome.SourceRows} were verified: rolled back, nothing was deleted.");
                    return;
                }
            }

            await kernel.CommitAsync(ct).ConfigureAwait(false);
            report.Changed = true;
        }
        catch
        {
            await kernel.RollbackAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task<long> RemoveAsync(PostgresSession kernel, ResolvedTable resolved, NeutralOperatorMoveScope scope, CancellationToken ct)
    {
        var table = resolved.Table;
        switch (table.Scope)
        {
            case MovedRowScope.OrderTree:
                return await RemoveOrdersAsync(kernel, table, scope, ct).ConfigureAwait(false);

            case MovedRowScope.OrderTargets:
                return await RekeyTargetsAsync(kernel, table, scope, ct).ConfigureAwait(false);

            default:
                var statement = new SqlStatement();
                var predicate = MovedRowSql.ScopePredicate(statement, table, scope);
                statement.Append($"DELETE FROM {table.QualifiedName} r WHERE {predicate}");
                return await kernel.ExecuteAsync(statement, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Deletes the moved orders level by level, deepest first (the parent key restricts deletes).</summary>
    private static async Task<long> RemoveOrdersAsync(PostgresSession kernel, MovedTable table, NeutralOperatorMoveScope scope, CancellationToken ct)
    {
        var depth = new SqlStatement();
        depth.Append(MovedRowSql.MovedTree(MovedRowSql.OrderTypes(depth, scope)) + "SELECT coalesce(max(depth), -1)::text FROM moved");
        var deepest = (int)await kernel.CountAsync(depth, ct).ConfigureAwait(false);

        long removed = 0;
        for (var level = deepest; level >= 0; level--)
        {
            var statement = new SqlStatement();
            var types = MovedRowSql.OrderTypes(statement, scope);
            statement.Append(
                MovedRowSql.MovedTree(types) +
                $"DELETE FROM {table.QualifiedName} r USING moved m WHERE r.{SqlStatement.Identifier(MovedTableCatalog.IdColumn)} = m.id " +
                $"AND m.depth = {statement.Integer(level)}");
            removed += await kernel.ExecuteAsync(statement, ct).ConfigureAwait(false);
        }

        return removed;
    }

    /// <summary>Re-keys the moved orders' kernel-vertical ledger entries to the NeutralOperator vertical (see the class summary).</summary>
    private static async Task<long> RekeyTargetsAsync(PostgresSession kernel, MovedTable table, NeutralOperatorMoveScope scope, CancellationToken ct)
    {
        string Column(string name) => SqlStatement.Identifier(name);
        var orders = SqlStatement.Qualified(MovedTableCatalog.KernelSchema, MovedTableCatalog.ServiceOrders);

        var statement = new SqlStatement();
        statement.Append(
            $"UPDATE {table.QualifiedName} r SET " +
            $"{Column(MovedTableCatalog.VerticalIdColumn)} = {statement.TextValue(NeutralOperatorMoveScope.VerticalId)}, " +
            $"{Column(MovedTableCatalog.KernelOrderIdColumn)} = NULL, " +
            $"{Column(MovedTableCatalog.OrderTypeIdColumn)} = o.{Column(MovedTableCatalog.OrderTypeIdColumn)}, " +
            $"{Column(MovedTableCatalog.ExclusivityColumn)} = p.exclusivity " +
            $"FROM {orders} o JOIN unnest({statement.TextArray(scope.OrderTypes.Select(t => t.Key))}, " +
            $"{statement.IntegerArray(scope.OrderTypes.Select(t => t.Exclusivity is { } level ? (int?)level : null))}) AS p(type_key, exclusivity) " +
            $"ON upper(o.{Column(MovedTableCatalog.OrderTypeIdColumn)}) = p.type_key " +
            $"WHERE o.{Column(MovedTableCatalog.IdColumn)} = r.{Column(MovedTableCatalog.ServiceOrderIdColumn)} " +
            $"AND r.{Column(MovedTableCatalog.VerticalIdColumn)} = {statement.TextValue(NeutralOperatorMoveScope.KernelVerticalId)}");
        return await kernel.ExecuteAsync(statement, ct).ConfigureAwait(false);
    }
}
