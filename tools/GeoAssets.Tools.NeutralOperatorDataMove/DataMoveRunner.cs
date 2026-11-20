namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>
/// One run against the two databases: preconditions (<see cref="SchemaPreflight"/>), then the verb.
/// <list type="bullet">
///   <item><c>plan</c> — the integrity checks and a classification of every moved row (identical, missing, different).</item>
///   <item><c>copy</c> — the same first; when nothing conflicts and nothing refuses, a second pass inserts the missing
///   rows table by table in foreign-key order, one target transaction per batch (re-runnable: a batch either landed whole
///   or not at all, and the next run skips what landed). A conflict found during that pass stops it before the tables
///   that would reference the rows it could not copy.</item>
///   <item><c>verify</c> — the classification; a missing row is a difference.</item>
///   <item><c>remove</c> — <see cref="KernelRemoval"/>.</item>
/// </list>
/// </summary>
internal static class DataMoveRunner
{
    /// <summary>The <see cref="PostgresSession.Role"/> of the kernel's database.</summary>
    public const string KernelRole = "kernel";

    /// <summary>The <see cref="PostgresSession.Role"/> of NeutralOperator.Server's database.</summary>
    public const string TargetRole = "neutral-operator";

    public static async Task<DataMoveReport> RunAsync(DataMoveOptions options, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        var scope = NeutralOperatorMoveScope.FromModule();
        var tables = MovedTableCatalog.Build();
        var kernel = await PostgresSession.OpenAsync(KernelRole, options.SourceConnectionString, ct).ConfigureAwait(false);
        await using (kernel.ConfigureAwait(false))
        {
            var target = await PostgresSession.OpenAsync(TargetRole, options.TargetConnectionString, ct).ConfigureAwait(false);
            await using (target.ConfigureAwait(false))
                return await RunAsync(kernel, target, scope, tables, options, ct).ConfigureAwait(false);
        }
    }

    internal static async Task<DataMoveReport> RunAsync(
        PostgresSession kernel,
        PostgresSession target,
        NeutralOperatorMoveScope scope,
        IReadOnlyList<MovedTable> tables,
        DataMoveOptions options,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(options);

        var report = new DataMoveReport(options.Verb, [.. scope.OrderTypes.Select(t => t.Id)], kernel.Description, target.Description);
        var resolved = await SchemaPreflight.RunAsync(kernel, target, tables, options.Verb, report, ct).ConfigureAwait(false);
        if (report.HasRefusal) return report;

        if (options.Verb == DataMoveVerb.Remove)
        {
            await KernelRemoval.RunAsync(kernel, target, resolved, scope, options, report, ct).ConfigureAwait(false);
            return report;
        }

        await IntegrityChecks.RunAsync(kernel, target, scope, options, report, ct).ConfigureAwait(false);
        report.SetTables(await ClassifyAsync(kernel, target, resolved, scope, options.BatchSize, copy: false, ct).ConfigureAwait(false));
        if (options.Verb != DataMoveVerb.Copy || report.HasRefusal || report.HasConflict || !report.HasMissing)
            return report;

        var copied = await ClassifyAsync(kernel, target, resolved, scope, options.BatchSize, copy: true, ct).ConfigureAwait(false);
        report.SetTables(copied);
        report.Changed = copied.Exists(t => t.Copied > 0);
        if (copied.Exists(t => t.Different > 0))
        {
            report.Add(DataMoveIssueSeverity.Conflict, "copy.stopped",
                "Rows changed while copying (writes must be frozen): the copy stopped after the first table with a conflict. Fix it and run copy again.");
        }

        return report;
    }

    /// <summary>Classifies (and, with <paramref name="copy"/>, copies) every table in order; a copy stops after a table with a conflict.</summary>
    internal static async Task<List<TableOutcome>> ClassifyAsync(
        PostgresSession kernel,
        PostgresSession target,
        IReadOnlyList<ResolvedTable> tables,
        NeutralOperatorMoveScope scope,
        int batchSize,
        bool copy,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(tables);

        var outcomes = new List<TableOutcome>(tables.Count);
        foreach (var table in tables)
        {
            var outcome = table.Table.Scope == MovedRowScope.OrderSequence
                ? await OrderSequenceMover.RunAsync(kernel, target, table, scope, batchSize, copy, ct).ConfigureAwait(false)
                : await KeyedTableMover.RunAsync(kernel, target, table, scope, batchSize, copy, ct).ConfigureAwait(false);
            outcomes.Add(outcome);
            if (copy && outcome.Different > 0) break;
        }

        return outcomes;
    }
}
