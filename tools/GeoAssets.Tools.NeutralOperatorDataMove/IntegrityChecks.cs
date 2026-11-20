using System.Globalization;

namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>
/// The relations a row-by-row comparison cannot see, checked on the kernel's database (and, for parents, the target's):
/// <list type="bullet">
///   <item><c>order.parent-not-moved</c> (conflict): a moved order whose parent neither moves nor is in the target — its
///   copy would break the target's foreign key;</item>
///   <item><c>order.unreachable</c> (conflict): a moved order its tree does not reach (a parent cycle, or deeper than the
///   tree goes);</item>
///   <item><c>order.child-not-moved</c>: an order of another type whose parent moves — <c>remove</c> cannot delete that
///   parent (a warning before, a refusal on remove);</item>
///   <item><c>occupancy.not-moved-order</c> (conflict): a change set staged on a target, or an approval saga of an order,
///   that does not move — the vertical's schema moves whole, so they could not follow their order;</item>
///   <item><c>ledger.already-keyed</c> (conflict): the kernel's ledger already holds a moved order's id under the
///   NeutralOperator vertical — <c>remove</c> re-keys the order's targets to exactly that;</item>
///   <item><c>outbox.pending</c> (plan, copy): undelivered messages the moved orders staged — <c>copy</c> refuses them
///   unless <c>--include-pending-outbox</c>; the kernel's own undelivered answers for them stay (info);</item>
///   <item><c>order-types.not-moved</c> (warning): order-type rows the kernel's database holds for NeutralOperator types.</item>
/// </list>
/// </summary>
internal static class IntegrityChecks
{
    public static async Task RunAsync(
        PostgresSession kernel, PostgresSession target, NeutralOperatorMoveScope scope, DataMoveOptions options, DataMoveReport report, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(report);

        await CheckParentsAsync(kernel, target, scope, options.BatchSize, report, ct).ConfigureAwait(false);
        await CheckReachAsync(kernel, scope, report, ct).ConfigureAwait(false);
        await CheckChildrenAsync(kernel, scope, options.Verb, report, ct).ConfigureAwait(false);
        await CheckVerticalRowsAsync(kernel, scope, report, ct).ConfigureAwait(false);
        await CheckLedgerAsync(kernel, scope, report, ct).ConfigureAwait(false);
        await CheckOutboxAsync(kernel, scope, options, report, ct).ConfigureAwait(false);
        await CheckOrderTypesAsync(kernel, scope, report, ct).ConfigureAwait(false);
    }

    private static string ServiceOrders => SqlStatement.Qualified(MovedTableCatalog.KernelSchema, MovedTableCatalog.ServiceOrders);

    private static string Column(string name) => SqlStatement.Identifier(name);

    private static async Task CheckParentsAsync(
        PostgresSession kernel, PostgresSession target, NeutralOperatorMoveScope scope, int batchSize, DataMoveReport report, CancellationToken ct)
    {
        var id = Column(MovedTableCatalog.IdColumn);
        var parent = Column(MovedTableCatalog.ParentOrderIdColumn);
        var absent = new List<string>();
        long absentCount = 0;
        string? after = null;
        while (true)
        {
            var statement = new SqlStatement();
            var types = MovedRowSql.OrderTypes(statement, scope);
            var keyset = after is null ? string.Empty : $" AND o.{parent}::text > {statement.TextValue(after)}";
            statement.Append(
                $"SELECT DISTINCT o.{parent}::text FROM {ServiceOrders} o WHERE {MovedRowSql.MovedOrderPredicate("o", types)} " +
                $"AND o.{parent} IS NOT NULL{keyset} " +
                $"AND NOT EXISTS (SELECT 1 FROM {ServiceOrders} p WHERE p.{id} = o.{parent} AND {MovedRowSql.MovedOrderPredicate("p", types)}) " +
                $"ORDER BY 1 LIMIT {statement.Integer(batchSize)}");
            var parents = (await kernel.QueryAsync(statement, ct).ConfigureAwait(false)).Select(r => r[0]!).ToList();
            if (parents.Count == 0) break;

            after = parents[^1];
            var lookup = new SqlStatement();
            lookup.Append($"SELECT r.{id}::text FROM {ServiceOrders} r WHERE r.{id} = ANY({lookup.TextArray(parents)})");
            var present = (await target.QueryAsync(lookup, ct).ConfigureAwait(false)).Select(r => r[0]).ToHashSet(StringComparer.Ordinal);
            foreach (var missing in parents.Where(p => !present.Contains(p)))
            {
                absentCount++;
                if (absent.Count < TableOutcome.MaxSamples) absent.Add(missing);
            }
        }

        if (absentCount > 0)
        {
            report.Add(DataMoveIssueSeverity.Conflict, "order.parent-not-moved", Invariant(
                $"{absentCount} parent order(s) of NeutralOperator orders are neither NeutralOperator orders nor in the target (e.g. {string.Join(", ", absent)}): their children cannot be copied."));
        }
    }

    private static async Task CheckReachAsync(PostgresSession kernel, NeutralOperatorMoveScope scope, DataMoveReport report, CancellationToken ct)
    {
        var statement = new SqlStatement();
        var types = MovedRowSql.OrderTypes(statement, scope);
        statement.Append(
            MovedRowSql.MovedTree(types) +
            $"SELECT ((SELECT count(*) FROM {ServiceOrders} o WHERE {MovedRowSql.MovedOrderPredicate("o", types)}) - (SELECT count(*) FROM moved))::text");
        var unreachable = await kernel.CountAsync(statement, ct).ConfigureAwait(false);
        if (unreachable > 0)
        {
            report.Add(DataMoveIssueSeverity.Conflict, "order.unreachable", Invariant(
                $"{unreachable} NeutralOperator order(s) sit in a parent cycle or too deep a hierarchy to be ordered parents-first."));
        }
    }

    private static async Task CheckChildrenAsync(
        PostgresSession kernel, NeutralOperatorMoveScope scope, DataMoveVerb verb, DataMoveReport report, CancellationToken ct)
    {
        var statement = new SqlStatement();
        var types = MovedRowSql.OrderTypes(statement, scope);
        statement.Append(
            $"SELECT count(*)::text FROM {ServiceOrders} c WHERE NOT ({MovedRowSql.MovedOrderPredicate("c", types)}) " +
            $"AND c.{Column(MovedTableCatalog.ParentOrderIdColumn)} IN ({MovedRowSql.MovedOrderIds(types)})");
        var children = await kernel.CountAsync(statement, ct).ConfigureAwait(false);
        if (children > 0)
        {
            report.Add(
                verb == DataMoveVerb.Remove ? DataMoveIssueSeverity.Refusal : DataMoveIssueSeverity.Warning,
                "order.child-not-moved",
                Invariant($"{children} order(s) of other types have a NeutralOperator parent: remove cannot delete those parents from the kernel's database."));
        }
    }

    private static async Task CheckVerticalRowsAsync(PostgresSession kernel, NeutralOperatorMoveScope scope, DataMoveReport report, CancellationToken ct)
    {
        var targets = SqlStatement.Qualified(MovedTableCatalog.KernelSchema, MovedTableCatalog.ServiceOrderTargets);
        var changeSets = new SqlStatement();
        var types = MovedRowSql.OrderTypes(changeSets, scope);
        changeSets.Append(
            $"SELECT count(*)::text FROM {SqlStatement.Qualified(MovedTableCatalog.VerticalSchema, MovedTableCatalog.OccupancyChangeSets)} s " +
            $"WHERE NOT EXISTS (SELECT 1 FROM {targets} t JOIN {ServiceOrders} o ON o.{Column(MovedTableCatalog.IdColumn)} = t.{Column(MovedTableCatalog.ServiceOrderIdColumn)} " +
            $"WHERE t.{Column(MovedTableCatalog.IdColumn)} = s.{Column(MovedTableCatalog.TargetIdColumn)} " +
            $"AND t.{Column(MovedTableCatalog.VerticalIdColumn)} = {changeSets.TextValue(NeutralOperatorMoveScope.KernelVerticalId)} " +
            $"AND {MovedRowSql.MovedOrderPredicate("o", types)})");
        var strayChangeSets = await kernel.CountAsync(changeSets, ct).ConfigureAwait(false);

        var sagas = new SqlStatement();
        types = MovedRowSql.OrderTypes(sagas, scope);
        sagas.Append(
            $"SELECT count(*)::text FROM {SqlStatement.Qualified(MovedTableCatalog.VerticalSchema, MovedTableCatalog.OccupancyApplySagas)} s " +
            $"WHERE s.{Column(MovedTableCatalog.ServiceOrderIdColumn)} NOT IN ({MovedRowSql.MovedOrderIds(types)})");
        var straySagas = await kernel.CountAsync(sagas, ct).ConfigureAwait(false);

        if (strayChangeSets > 0 || straySagas > 0)
        {
            report.Add(DataMoveIssueSeverity.Conflict, "occupancy.not-moved-order", Invariant(
                $"{strayChangeSets} occupancy change set(s) and {straySagas} approval saga(s) in the kernel's {MovedTableCatalog.VerticalSchema} schema belong to orders that are not NeutralOperator orders' kernel targets: they cannot follow their order."));
        }
    }

    private static async Task CheckLedgerAsync(PostgresSession kernel, NeutralOperatorMoveScope scope, DataMoveReport report, CancellationToken ct)
    {
        var statement = new SqlStatement();
        var types = MovedRowSql.OrderTypes(statement, scope);
        statement.Append(
            $"SELECT count(*)::text FROM {SqlStatement.Qualified(MovedTableCatalog.KernelSchema, MovedTableCatalog.ServiceOrderTargets)} t " +
            $"WHERE t.{Column(MovedTableCatalog.VerticalIdColumn)} = {statement.TextValue(NeutralOperatorMoveScope.VerticalId)} " +
            $"AND t.{Column(MovedTableCatalog.ServiceOrderIdColumn)} IN ({MovedRowSql.MovedOrderIds(types)})");
        var keyed = await kernel.CountAsync(statement, ct).ConfigureAwait(false);
        if (keyed > 0)
        {
            report.Add(DataMoveIssueSeverity.Conflict, "ledger.already-keyed", Invariant(
                $"The kernel's target ledger already holds {keyed} entr(ies) of moved orders under the '{NeutralOperatorMoveScope.VerticalId}' vertical: remove could not re-key the orders' kernel targets there."));
        }
    }

    private static async Task CheckOutboxAsync(
        PostgresSession kernel, NeutralOperatorMoveScope scope, DataMoveOptions options, DataMoveReport report, CancellationToken ct)
    {
        var outbox = SqlStatement.Qualified(MovedTableCatalog.KernelSchema, "WorkflowOutboxMessages");
        var status = Column(MovedTableCatalog.StatusColumn);
        var messageType = Column(MovedTableCatalog.MessageTypeColumn);
        var partition = Column(MovedTableCatalog.PartitionKeyColumn);

        var own = new SqlStatement();
        var types = MovedRowSql.OrderTypes(own, scope);
        own.Append(
            $"SELECT count(*)::text FROM {outbox} r WHERE r.{status} <> {own.Integer(NeutralOperatorMoveScope.DispatchedStatus)} " +
            $"AND r.{messageType} = ANY({own.TextArray(NeutralOperatorMoveScope.MessageTypes)}) AND r.{partition} IN ({MovedRowSql.MovedOrderIds(types)})");
        var pending = await kernel.CountAsync(own, ct).ConfigureAwait(false);
        // verify and remove compare these messages like any other moved row: one the target lacks is a difference.
        var copying = options.Verb is DataMoveVerb.Plan or DataMoveVerb.Copy;
        if (copying && pending > 0 && !options.IncludePendingOutbox)
        {
            var refuse = options.Verb == DataMoveVerb.Copy;
            report.Add(
                refuse ? DataMoveIssueSeverity.Refusal : DataMoveIssueSeverity.Warning,
                "outbox.pending",
                Invariant(
                    $"The kernel's outbox holds {pending} undelivered message(s) the moved orders staged (e.g. a pending approval's asset mutation batch): {(refuse ? "copy refuses them" : "copy will refuse them")} unless --include-pending-outbox, which copies them for NeutralOperator.Server's relay (it needs a broker) to deliver."));
        }
        else if (copying && pending > 0)
        {
            report.Add(DataMoveIssueSeverity.Info, "outbox.pending", Invariant(
                $"{pending} undelivered message(s) of the moved orders are copied with them; a consumer deduplicates a message both relays deliver by its id."));
        }

        var answers = new SqlStatement();
        types = MovedRowSql.OrderTypes(answers, scope);
        answers.Append(
            $"SELECT count(*)::text FROM {outbox} r WHERE r.{status} <> {answers.Integer(NeutralOperatorMoveScope.DispatchedStatus)} " +
            $"AND r.{messageType} <> ALL({answers.TextArray(NeutralOperatorMoveScope.MessageTypes)}) AND r.{partition} IN ({MovedRowSql.MovedOrderIds(types)})");
        var kernelAnswers = await kernel.CountAsync(answers, ct).ConfigureAwait(false);
        if (kernelAnswers > 0)
        {
            report.Add(DataMoveIssueSeverity.Info, "outbox.kernel-answers", Invariant(
                $"{kernelAnswers} undelivered kernel message(s) about moved orders (the catalog's answers to their batches) stay in the kernel's outbox; its relay delivers them over the broker."));
        }
    }

    private static async Task CheckOrderTypesAsync(PostgresSession kernel, NeutralOperatorMoveScope scope, DataMoveReport report, CancellationToken ct)
    {
        var statement = new SqlStatement();
        statement.Append(
            $"SELECT count(*)::text FROM {SqlStatement.Qualified(MovedTableCatalog.KernelSchema, MovedTableCatalog.OrderTypes)} t " +
            $"WHERE upper(t.{Column(MovedTableCatalog.IdColumn)}) = ANY({MovedRowSql.OrderTypes(statement, scope)})");
        var overrides = await kernel.CountAsync(statement, ct).ConfigureAwait(false);
        if (overrides > 0)
        {
            report.Add(DataMoveIssueSeverity.Warning, "order-types.not-moved", Invariant(
                $"The kernel's database holds {overrides} order-type row(s) for NeutralOperator types, which do not move: NeutralOperator.Server builds its types from the module's code; re-create an override through its order-type API if it is still wanted."));
        }
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
