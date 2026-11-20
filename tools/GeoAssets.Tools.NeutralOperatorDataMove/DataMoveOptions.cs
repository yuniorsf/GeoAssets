namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>
/// One run's options. A class rather than a record on purpose: a record's generated <c>ToString</c> would print the
/// connection strings, which carry passwords — nothing in this tool ever writes them anywhere.
/// </summary>
internal sealed class DataMoveOptions
{
    /// <summary>Environment variable holding the kernel (source) connection string when <c>--source</c> is not given.</summary>
    public const string SourceVariable = "GEOASSETS_KERNEL_DB";

    /// <summary>Environment variable holding the NeutralOperator (target) connection string when <c>--target</c> is not given.</summary>
    public const string TargetVariable = "GEOASSETS_NEUTRAL_OPERATOR_DB";

    /// <summary>Rows (or, for the action logs and dispatches, orders) read per page and copied per transaction.</summary>
    public const int DefaultBatchSize = 500;

    /// <summary>The largest <see cref="BatchSize"/> accepted.</summary>
    public const int MaxBatchSize = 10_000;

    public required DataMoveVerb Verb { get; init; }

    /// <summary>The kernel's GeoAssets database (where the orders are).</summary>
    public required string SourceConnectionString { get; init; }

    /// <summary>NeutralOperator.Server's database (where they go).</summary>
    public required string TargetConnectionString { get; init; }

    public int BatchSize { get; init; } = DefaultBatchSize;

    /// <summary>Copy the moved orders' undelivered outbox messages too; without it <c>copy</c> refuses while there are any.</summary>
    public bool IncludePendingOutbox { get; init; }

    public override string ToString() => $"{Verb} (batch size {BatchSize}{(IncludePendingOutbox ? ", pending outbox included" : string.Empty)})";

    public static string Usage =>
        $"""
        Usage: GeoAssets.Tools.NeutralOperatorDataMove [plan|copy|verify|remove] [options]

        Moves the NeutralOperator service orders the kernel hosted — with their dispatches, action logs, targets and the
        whole neutral_operator schema — from the kernel's GeoAssets database to NeutralOperator.Server's database, keeping
        every id, version, timestamp and organization id. See deploy/neutral-operator/README.md ("Moving the kernel's
        NeutralOperator orders") for the runbook.

        Verbs:
          plan     (default) dry run: rows per table, conflicts and preconditions; writes nothing.
          copy     inserts the rows the target lacks (insert-if-absent, one transaction per batch); identical rows are
                   skipped, a row present with other content is a conflict and is never overwritten.
          verify   every moved row is in the target with identical content.
          remove   re-verifies inside one kernel transaction and deletes the moved rows from the kernel; refuses (nothing
                   deleted) when any row differs or is missing in the target. Needs --confirm.

        Options:
          --source <connection string>   the kernel's database (else ${SourceVariable})
          --target <connection string>   NeutralOperator.Server's database (else ${TargetVariable})
          --batch-size <n>               rows per page and per copy transaction, 1..{MaxBatchSize} (default {DefaultBatchSize})
          --include-pending-outbox       plan/copy: also copy the moved orders' undelivered outbox messages
          --confirm                      remove: really delete
          --help                         this text

        Connect as the owner of the tables (or a superuser) on both databases: row-level security must not filter the
        rows, and copy bypasses the target's user triggers to keep the stored row versions.

        Exit codes: 0 done, nothing wrong · 1 usage error · 2 conflicts or differences found · 3 refused (a
        precondition failed; nothing changed) · 4 failed (database error or cancelled; rolled back).
        """;
}
