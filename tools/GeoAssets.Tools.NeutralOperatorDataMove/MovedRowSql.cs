using System.Globalization;
using System.Text;

namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>
/// The statements that select, look up, insert and remove moved rows. Which rows move is written once, here:
/// <list type="bullet">
///   <item>an order moves when <c>upper("OrderTypeId")</c> is a NeutralOperator order type's (<see cref="MovedOrderPredicate"/>);
///   its tree (<see cref="MovedTree"/>) gives each a depth — 0 for an order whose parent does not move — so parents are
///   copied first and removed last;</item>
///   <item>its dispatches and action logs are the rows of those orders; its targets the ones it holds under the kernel
///   vertical; its outbox messages the undelivered ones of the module's message types partitioned by its id;</item>
///   <item>every row of the vertical's schema moves.</item>
/// </list>
/// Every column is read as text and written back from it, cast to the column's base type.
/// </summary>
internal static class MovedRowSql
{
    private const string Row = "r";
    private const int MaxTreeDepth = 64;

    private static string ServiceOrders => SqlStatement.Qualified(MovedTableCatalog.KernelSchema, MovedTableCatalog.ServiceOrders);

    private static string Id => SqlStatement.Identifier(MovedTableCatalog.IdColumn);

    private static string OrderTypeId => SqlStatement.Identifier(MovedTableCatalog.OrderTypeIdColumn);

    private static string ParentOrderId => SqlStatement.Identifier(MovedTableCatalog.ParentOrderIdColumn);

    private static string ServiceOrderId => SqlStatement.Identifier(MovedTableCatalog.ServiceOrderIdColumn);

    /// <summary>The order type keys as a parameter of <paramref name="statement"/>, to reuse in several predicates.</summary>
    public static string OrderTypes(SqlStatement statement, NeutralOperatorMoveScope scope) =>
        statement.TextArray(scope.OrderTypeKeys);

    /// <summary><c>upper(alias."OrderTypeId") = ANY(types)</c>: the order aliased <paramref name="alias"/> moves.</summary>
    public static string MovedOrderPredicate(string alias, string types) => $"upper({alias}.{OrderTypeId}) = ANY({types})";

    /// <summary>The ids of the moved orders, as a subquery.</summary>
    public static string MovedOrderIds(string types) =>
        $"SELECT mo.{Id} FROM {ServiceOrders} mo WHERE {MovedOrderPredicate("mo", types)}";

    /// <summary><c>WITH RECURSIVE moved(id, depth)</c>: every moved order reachable from a root, with its depth.</summary>
    public static string MovedTree(string types) =>
        $"WITH RECURSIVE moved(id, depth) AS (" +
        $"SELECT o.{Id}, 0 FROM {ServiceOrders} o WHERE {MovedOrderPredicate("o", types)} " +
        $"AND NOT EXISTS (SELECT 1 FROM {ServiceOrders} p WHERE p.{Id} = o.{ParentOrderId} AND {MovedOrderPredicate("p", types)}) " +
        $"UNION ALL SELECT c.{Id}, m.depth + 1 FROM {ServiceOrders} c JOIN moved m ON c.{ParentOrderId} = m.id " +
        $"WHERE {MovedOrderPredicate("c", types)} AND m.depth < {MaxTreeDepth.ToString(CultureInfo.InvariantCulture)}) ";

    /// <summary>The predicate on <c>r</c> selecting a table's moved rows (not for <see cref="MovedRowScope.OrderTree"/>).</summary>
    public static string ScopePredicate(SqlStatement statement, MovedTable table, NeutralOperatorMoveScope scope)
    {
        ArgumentNullException.ThrowIfNull(statement);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(scope);

        return table.Scope switch
        {
            MovedRowScope.OrderSequence =>
                $"{Row}.{ServiceOrderId} IN ({MovedOrderIds(OrderTypes(statement, scope))})",
            MovedRowScope.OrderTargets =>
                $"{Row}.{SqlStatement.Identifier(MovedTableCatalog.VerticalIdColumn)} = {statement.TextValue(NeutralOperatorMoveScope.KernelVerticalId)} " +
                $"AND {Row}.{ServiceOrderId} IN ({MovedOrderIds(OrderTypes(statement, scope))})",
            MovedRowScope.PendingOutbox =>
                $"{Row}.{SqlStatement.Identifier(MovedTableCatalog.StatusColumn)} <> {statement.Integer(NeutralOperatorMoveScope.DispatchedStatus)} " +
                $"AND {Row}.{SqlStatement.Identifier(MovedTableCatalog.MessageTypeColumn)} = ANY({statement.TextArray(NeutralOperatorMoveScope.MessageTypes)}) " +
                $"AND {Row}.{SqlStatement.Identifier(MovedTableCatalog.PartitionKeyColumn)} IN ({MovedOrderIds(OrderTypes(statement, scope))})",
            MovedRowScope.WholeTable => "TRUE",
            _ => throw new InvalidOperationException($"{table.DisplayName}'s rows are not selected by a predicate."),
        };
    }

    /// <summary>
    /// One page of a keyed table's moved rows, after <paramref name="after"/> (the previous page's last
    /// <see cref="PageKey"/>), in page-key order. An <see cref="MovedRowScope.OrderTree"/> row starts with its depth.
    /// </summary>
    public static SqlStatement SourcePage(ResolvedTable resolved, NeutralOperatorMoveScope scope, IReadOnlyList<string?>? after, int limit)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        var table = resolved.Table;
        var statement = new SqlStatement();
        string keyset;
        if (table.Scope == MovedRowScope.OrderTree)
        {
            var types = OrderTypes(statement, scope);
            keyset = after is null
                ? string.Empty
                : $"WHERE (m.depth, {Row}.{Id}) > ({statement.TextValue(after[0])}::integer, {statement.TextValue(after[1])}::{resolved.TypeOf(MovedTableCatalog.IdColumn)}) ";
            statement.Append(
                MovedTree(types) +
                $"SELECT m.depth::text, {SelectColumns(table)} FROM moved m JOIN {table.QualifiedName} {Row} ON {Row}.{Id} = m.id " +
                keyset +
                $"ORDER BY m.depth, {Row}.{Id} LIMIT {statement.Integer(limit)}");
            return statement;
        }

        var predicate = ScopePredicate(statement, table, scope);
        keyset = after is null ? string.Empty : $" AND ({KeyList(table)}) > ({KeyValues(statement, resolved, after)})";
        statement.Append(
            $"SELECT {SelectColumns(table)} FROM {table.QualifiedName} {Row} WHERE {predicate}{keyset} " +
            $"ORDER BY {KeyList(table)} LIMIT {statement.Integer(limit)}");
        return statement;
    }

    /// <summary>How many leading values of a <see cref="SourcePage"/> row precede the table's columns.</summary>
    public static int Offset(MovedTable table) => table?.Scope == MovedRowScope.OrderTree ? 1 : 0;

    /// <summary>A <see cref="SourcePage"/> row's page key: (depth, id) for the order tree, else the primary key.</summary>
    public static IReadOnlyList<string?> PageKey(MovedTable table, string?[] row)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(row);

        return table.Scope == MovedRowScope.OrderTree
            ? [row[0], row[1 + table.IndexOf(MovedTableCatalog.IdColumn)]]
            : [.. table.KeyColumns.Select(k => row[table.IndexOf(k)])];
    }

    /// <summary>The target's rows with the primary keys of <paramref name="keys"/> (each one value per key column).</summary>
    public static SqlStatement TargetRows(ResolvedTable resolved, IReadOnlyList<IReadOnlyList<string?>> keys)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(keys);

        var table = resolved.Table;
        var statement = new SqlStatement();
        var arrays = table.KeyColumns
            .Select((column, i) => $"{statement.TextArray(keys.Select(k => k[i]))}::{resolved.TypeOf(column)}[]")
            .ToList();
        statement.Append(
            $"SELECT {SelectColumns(table)} FROM {table.QualifiedName} {Row} " +
            $"WHERE ({KeyList(table)}) IN (SELECT * FROM unnest({string.Join(", ", arrays)}))");
        return statement;
    }

    /// <summary>
    /// The keys (as <see cref="RowValues.Key"/> parts, one column each) of the <paramref name="rows"/> that would collide
    /// with another row of the target on <paramref name="index"/>: same index values, a different primary key, both rows
    /// inside a partial index's predicate. The rows travel as arrays unnested into a relation with the table's own column
    /// names, so the model's predicate text applies to them as written — and, inside the <c>EXISTS</c>, to the target row,
    /// the innermost relation holding those names.
    /// </summary>
    public static SqlStatement UniqueCollisions(ResolvedTable resolved, UniqueIndex index, IReadOnlyList<string?[]> rows)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(rows);

        var table = resolved.Table;
        var statement = new SqlStatement();
        var arrays = table.Columns
            .Select((column, i) => $"{statement.TextArray(rows.Select(r => r[i]))}::{resolved.TypeOf(column)}[]")
            .ToList();
        var names = string.Join(", ", table.Columns.Select(SqlStatement.Identifier));
        var filter = index.Filter is null ? string.Empty : $"({index.Filter}) AND ";
        var same = string.Join(" AND ", index.Columns.Select(c => $"{Row}.{SqlStatement.Identifier(c)} = s.{SqlStatement.Identifier(c)}"));
        var otherKey = $"({KeyList(table)}) <> ({string.Join(", ", table.KeyColumns.Select(k => $"s.{SqlStatement.Identifier(k)}"))})";
        statement.Append(
            $"SELECT {string.Join(", ", table.KeyColumns.Select(k => $"s.{SqlStatement.Identifier(k)}::text"))} " +
            $"FROM (SELECT * FROM unnest({string.Join(", ", arrays)}) AS u({names})) s " +
            $"WHERE {filter}EXISTS (SELECT 1 FROM {table.QualifiedName} {Row} WHERE {filter}{same} AND {otherKey})");
        return statement;
    }

    /// <summary>The moved order ids after <paramref name="after"/>, in id order (the history tables' pages).</summary>
    public static SqlStatement MovedOrderPage(NeutralOperatorMoveScope scope, string? after, int limit)
    {
        var statement = new SqlStatement();
        var types = OrderTypes(statement, scope);
        var keyset = after is null ? string.Empty : $" AND o.{Id}::text > {statement.TextValue(after)}";
        statement.Append(
            $"SELECT o.{Id}::text FROM {ServiceOrders} o WHERE {MovedOrderPredicate("o", types)}{keyset} " +
            $"ORDER BY o.{Id}::text LIMIT {statement.Integer(limit)}");
        return statement;
    }

    /// <summary>A history table's rows of <paramref name="orderIds"/>, by order then identity — on either database.</summary>
    public static SqlStatement RowsOfOrders(ResolvedTable resolved, IReadOnlyList<string> orderIds)
    {
        ArgumentNullException.ThrowIfNull(resolved);

        var table = resolved.Table;
        var statement = new SqlStatement();
        var ids = statement.TextArray(orderIds);
        statement.Append(
            $"SELECT {SelectColumns(table)} FROM {table.QualifiedName} {Row} " +
            $"WHERE {Row}.{ServiceOrderId} = ANY({ids}::{resolved.TypeOf(MovedTableCatalog.ServiceOrderIdColumn)}[]) " +
            $"ORDER BY {Row}.{ServiceOrderId}, {KeyList(table)}");
        return statement;
    }

    /// <summary>
    /// <c>INSERT</c> of <paramref name="rows"/> (each in <see cref="MovedTable.Columns"/> order) writing
    /// <see cref="MovedTable.CopiedColumns"/>; <paramref name="skipConflicts"/> adds <c>ON CONFLICT DO NOTHING</c>, which
    /// skips a row colliding on any unique index — the caller counts what was really written.
    /// </summary>
    public static SqlStatement Insert(ResolvedTable resolved, IReadOnlyList<string?[]> rows, bool skipConflicts)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        ArgumentNullException.ThrowIfNull(rows);

        var table = resolved.Table;
        var statement = new SqlStatement();
        var indexes = table.CopiedColumns.Select(table.IndexOf).ToList();
        var text = new StringBuilder()
            .Append("INSERT INTO ").Append(table.QualifiedName)
            .Append(" (").AppendJoin(", ", table.CopiedColumns.Select(SqlStatement.Identifier)).Append(") VALUES ");
        for (var r = 0; r < rows.Count; r++)
        {
            text.Append(r == 0 ? "(" : ", (");
            for (var c = 0; c < indexes.Count; c++)
            {
                if (c > 0) text.Append(", ");
                text.Append(statement.TextValue(rows[r][indexes[c]])).Append("::").Append(resolved.TypeOf(table.CopiedColumns[c]));
            }

            text.Append(')');
        }

        if (skipConflicts) text.Append(" ON CONFLICT DO NOTHING");
        statement.Append(text.ToString());
        return statement;
    }

    /// <summary>The enabled user triggers (name, mode) of a table — what copy disables for its insert and restores after.</summary>
    public static SqlStatement EnabledUserTriggers(MovedTable table)
    {
        ArgumentNullException.ThrowIfNull(table);

        var statement = new SqlStatement();
        statement.Append(
            "SELECT t.tgname::text, t.tgenabled::text FROM pg_trigger t " +
            $"WHERE t.tgrelid = to_regclass({statement.TextValue(table.QualifiedName)}) AND NOT t.tgisinternal AND t.tgenabled IN ('O', 'A') ORDER BY 1");
        return statement;
    }

    /// <summary><c>r."k1", r."k2"</c>.</summary>
    private static string KeyList(MovedTable table) =>
        string.Join(", ", table.KeyColumns.Select(k => $"{Row}.{SqlStatement.Identifier(k)}"));

    private static string KeyValues(SqlStatement statement, ResolvedTable resolved, IReadOnlyList<string?> values) =>
        string.Join(", ", resolved.Table.KeyColumns.Select((k, i) => $"{statement.TextValue(values[i])}::{resolved.TypeOf(k)}"));

    /// <summary><c>r."c1"::text, r."c2"::text, …</c> in <see cref="MovedTable.Columns"/> order.</summary>
    private static string SelectColumns(MovedTable table) =>
        string.Join(", ", table.Columns.Select(c => $"{Row}.{SqlStatement.Identifier(c)}::text"));
}
