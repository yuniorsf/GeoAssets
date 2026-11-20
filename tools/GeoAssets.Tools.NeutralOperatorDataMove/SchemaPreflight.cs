using GeoAssets.NeutralOperator.Persistence;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>
/// What must hold before any row is read: both databases at the migration heads this tool knows, every moved table with
/// exactly the model's columns and the same base types on both sides, no table in the vertical's schema this tool does
/// not know, row-level security unable to filter a row, and the privileges the verb needs. Each failure is a
/// <see cref="DataMoveIssueSeverity.Refusal"/>.
/// </summary>
internal static class SchemaPreflight
{
    public static async Task<IReadOnlyList<ResolvedTable>> RunAsync(
        PostgresSession kernel, PostgresSession target, IReadOnlyList<MovedTable> tables, DataMoveVerb verb, DataMoveReport report, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(kernel);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(report);

        await CheckServiceOrderChainAsync(kernel, RequiredMigrations.KernelServiceOrders, report, ct).ConfigureAwait(false);
        await CheckServiceOrderChainAsync(target, RequiredMigrations.NeutralOperatorServerServiceOrders, report, ct).ConfigureAwait(false);
        await CheckVerticalChainAsync(kernel, report, ct).ConfigureAwait(false);
        await CheckVerticalChainAsync(target, report, ct).ConfigureAwait(false);
        await CheckUnknownVerticalTablesAsync(kernel, tables, report, ct).ConfigureAwait(false);
        await CheckUnknownVerticalTablesAsync(target, tables, report, ct).ConfigureAwait(false);

        var resolved = new List<ResolvedTable>(tables.Count);
        foreach (var table in tables)
        {
            var source = await ColumnsAsync(kernel, table, report, ct).ConfigureAwait(false);
            var destination = await ColumnsAsync(target, table, report, ct).ConfigureAwait(false);
            if (source is null || destination is null) continue;

            var types = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var column in table.Columns)
            {
                var (sourceBase, sourceFull) = source[column];
                var (targetBase, targetFull) = destination[column];
                if (!string.Equals(sourceBase, targetBase, StringComparison.Ordinal))
                {
                    report.Add(DataMoveIssueSeverity.Refusal, "schema.type-mismatch",
                        $"{table.DisplayName}.{column} is {sourceFull} in the kernel's database but {targetFull} in NeutralOperator.Server's.");
                }
                else if (!string.Equals(sourceFull, targetFull, StringComparison.Ordinal))
                {
                    report.Add(DataMoveIssueSeverity.Warning, "schema.modifier-mismatch",
                        $"{table.DisplayName}.{column} is {sourceFull} in the kernel's database but {targetFull} in NeutralOperator.Server's; a value that does not fit fails the copy, a rounded one fails verify.");
                }

                types[column] = targetBase;
            }

            resolved.Add(new ResolvedTable(table, types));
        }

        await CheckAccessAsync(kernel, tables, verb, isTarget: false, report, ct).ConfigureAwait(false);
        await CheckAccessAsync(target, tables, verb, isTarget: true, report, ct).ConfigureAwait(false);
        return resolved;
    }

    private static async Task CheckServiceOrderChainAsync(PostgresSession session, string head, DataMoveReport report, CancellationToken ct)
    {
        var history = SqlStatement.Qualified(MovedTableCatalog.KernelSchema, RequiredMigrations.ServiceOrderHistoryTable);
        if (!await ReadableAsync(session, history, report, ct).ConfigureAwait(false))
            return;

        var applied = new SqlStatement();
        applied.Append($"SELECT count(*)::text FROM {history} WHERE \"MigrationId\" = {applied.TextValue(head)}");
        if (await session.CountAsync(applied, ct).ConfigureAwait(false) == 0)
        {
            report.Add(DataMoveIssueSeverity.Refusal, "migrations.service-orders",
                $"The {session.Role} database's ServiceOrderDbContext chain is not at {head}: apply it first.");
        }
    }

    private static async Task CheckVerticalChainAsync(PostgresSession session, DataMoveReport report, CancellationToken ct)
    {
        var history = SqlStatement.Qualified(MovedTableCatalog.VerticalSchema, MovedTableCatalog.VerticalHistoryTable);
        if (!await ReadableAsync(session, history, report, ct).ConfigureAwait(false))
            return;

        var options = new DbContextOptionsBuilder<NeutralOperatorDbContext>()
            .UseNpgsql(session.ConnectionString, NeutralOperatorDbContext.ConfigureMigrations)
            .Options;
        var context = new NeutralOperatorDbContext(options);
        await using (context.ConfigureAwait(false))
        {
            var known = context.Database.GetMigrations().ToList();
            var applied = (await context.Database.GetAppliedMigrationsAsync(ct).ConfigureAwait(false)).ToList();
            var pending = known.Except(applied, StringComparer.Ordinal).ToList();
            var unknown = applied.Except(known, StringComparer.Ordinal).ToList();
            if (pending.Count > 0)
            {
                report.Add(DataMoveIssueSeverity.Refusal, "migrations.neutral-operator",
                    $"The {session.Role} database's NeutralOperatorDbContext chain is missing {string.Join(", ", pending)}: apply it first.");
            }

            if (unknown.Count > 0)
            {
                report.Add(DataMoveIssueSeverity.Refusal, "migrations.neutral-operator",
                    $"The {session.Role} database's NeutralOperatorDbContext chain has {string.Join(", ", unknown)}, which this build of the tool does not know: use the tool built from the same revision.");
            }
        }
    }

    /// <summary>Whether a migrations history table exists and the role may read it; a refusal otherwise.</summary>
    private static async Task<bool> ReadableAsync(PostgresSession session, string history, DataMoveReport report, CancellationToken ct)
    {
        var statement = new SqlStatement();
        var name = statement.TextValue(history);
        statement.Append(
            $"SELECT CASE WHEN to_regclass({name}) IS NULL THEN 'missing' " +
            $"WHEN has_schema_privilege((SELECT relnamespace FROM pg_class WHERE oid = to_regclass({name})), 'USAGE') " +
            $"AND has_table_privilege(to_regclass({name}), 'SELECT') THEN 'readable' ELSE 'denied' END");
        var state = await session.ScalarAsync(statement, ct).ConfigureAwait(false);
        if (string.Equals(state, "readable", StringComparison.Ordinal)) return true;

        report.Add(
            DataMoveIssueSeverity.Refusal,
            string.Equals(state, "missing", StringComparison.Ordinal) ? "migrations.missing" : "privileges.missing",
            string.Equals(state, "missing", StringComparison.Ordinal)
                ? $"The {session.Role} database has no {history}: apply its migration chains first (see deploy/neutral-operator/README.md)."
                : $"The connected role cannot read {history} in the {session.Role} database: connect as the tables' owner.");
        return false;
    }

    private static async Task CheckUnknownVerticalTablesAsync(PostgresSession session, IReadOnlyList<MovedTable> tables, DataMoveReport report, CancellationToken ct)
    {
        var statement = new SqlStatement();
        statement.Append(
            "SELECT c.relname::text FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace " +
            $"WHERE n.nspname = {statement.TextValue(MovedTableCatalog.VerticalSchema)} AND c.relkind IN ('r', 'p') ORDER BY 1");
        var known = tables
            .Where(t => string.Equals(t.Schema, MovedTableCatalog.VerticalSchema, StringComparison.Ordinal))
            .Select(t => t.Name)
            .Append(MovedTableCatalog.VerticalHistoryTable)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var row in await session.QueryAsync(statement, ct).ConfigureAwait(false))
        {
            if (row[0] is { } name && !known.Contains(name))
            {
                report.Add(DataMoveIssueSeverity.Refusal, "schema.unknown-table",
                    $"The {session.Role} database's {MovedTableCatalog.VerticalSchema} schema holds {name}, which this tool does not move: update the tool.");
            }
        }
    }

    /// <summary>The table's columns as (base type, full type) — null, with a refusal, when they are not the model's.</summary>
    private static async Task<Dictionary<string, (string Base, string Full)>?> ColumnsAsync(
        PostgresSession session, MovedTable table, DataMoveReport report, CancellationToken ct)
    {
        var statement = new SqlStatement();
        statement.Append(
            "SELECT a.attname::text, format_type(a.atttypid, NULL), format_type(a.atttypid, a.atttypmod) FROM pg_attribute a " +
            $"WHERE a.attrelid = to_regclass({statement.TextValue(table.QualifiedName)}) AND a.attnum > 0 AND NOT a.attisdropped");
        var rows = await session.QueryAsync(statement, ct).ConfigureAwait(false);
        if (rows.Count == 0)
        {
            report.Add(DataMoveIssueSeverity.Refusal, "schema.table-missing", $"The {session.Role} database has no {table.DisplayName}.");
            return null;
        }

        var columns = rows.ToDictionary(r => r[0]!, r => (r[1]!, r[2]!), StringComparer.Ordinal);
        var unmapped = columns.Keys.Where(c => !table.Columns.Contains(c, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();
        var absent = table.Columns.Where(c => !columns.ContainsKey(c)).ToList();
        if (unmapped.Count > 0)
        {
            report.Add(DataMoveIssueSeverity.Refusal, "schema.column-drift",
                $"The {session.Role} database's {table.DisplayName} has {string.Join(", ", unmapped)}, which the EF model does not map: the move would drop them.");
        }

        if (absent.Count > 0)
        {
            report.Add(DataMoveIssueSeverity.Refusal, "schema.column-drift",
                $"The {session.Role} database's {table.DisplayName} lacks {string.Join(", ", absent)}: it is not at the model's head.");
        }

        return unmapped.Count > 0 || absent.Count > 0 ? null : columns;
    }

    /// <summary>
    /// Row-level security must not apply to the connected role on any moved table (it is the table owner without
    /// <c>FORCE ROW LEVEL SECURITY</c>, a superuser, or has <c>BYPASSRLS</c>); the role needs SELECT everywhere, INSERT on
    /// the target for copy, DELETE (and UPDATE on the targets, which are re-keyed) on the kernel for remove; and copy must
    /// own the target tables whose user triggers it bypasses.
    /// </summary>
    private static async Task CheckAccessAsync(
        PostgresSession session, IReadOnlyList<MovedTable> tables, DataMoveVerb verb, bool isTarget, DataMoveReport report, CancellationToken ct)
    {
        var role = new SqlStatement();
        role.Append("SELECT rolsuper::text, rolbypassrls::text, current_user::text FROM pg_roles WHERE rolname = current_user");
        var roleRow = (await session.QueryAsync(role, ct).ConfigureAwait(false)).FirstOrDefault();
        var superuser = roleRow is not null && string.Equals(roleRow[0], "true", StringComparison.Ordinal);
        var bypass = superuser || (roleRow is not null && string.Equals(roleRow[1], "true", StringComparison.Ordinal));
        var roleName = roleRow?[2] ?? "the connected role";

        foreach (var table in tables)
        {
            var statement = new SqlStatement();
            var name = statement.TextValue(table.QualifiedName);
            statement.Append(
                "SELECT c.relrowsecurity::text, c.relforcerowsecurity::text, pg_has_role(c.relowner, 'USAGE')::text, " +
                "has_table_privilege(c.oid, 'SELECT')::text, has_table_privilege(c.oid, 'INSERT')::text, " +
                "has_table_privilege(c.oid, 'UPDATE')::text, has_table_privilege(c.oid, 'DELETE')::text, " +
                "EXISTS (SELECT 1 FROM pg_trigger t WHERE t.tgrelid = c.oid AND NOT t.tgisinternal AND t.tgenabled IN ('O', 'A'))::text " +
                $"FROM pg_class c WHERE c.oid = to_regclass({name})");
            var row = (await session.QueryAsync(statement, ct).ConfigureAwait(false)).FirstOrDefault();
            if (row is null) continue;

            bool Flag(int i) => string.Equals(row[i], "true", StringComparison.Ordinal);
            var owner = superuser || Flag(2);
            if (Flag(0) && !bypass && (Flag(1) || !owner))
            {
                report.Add(DataMoveIssueSeverity.Refusal, "rls.filtered",
                    $"Row-level security would filter {table.DisplayName} in the {session.Role} database for role {roleName}: connect as the table owner, a superuser or a role with BYPASSRLS.");
            }

            var needed = new List<string>();
            if (!Flag(3)) needed.Add("SELECT");
            if (isTarget && verb == DataMoveVerb.Copy && !Flag(4)) needed.Add("INSERT");
            if (!isTarget && verb == DataMoveVerb.Remove && table.Scope == MovedRowScope.OrderTargets && !Flag(5)) needed.Add("UPDATE");
            if (!isTarget && verb == DataMoveVerb.Remove && !Flag(6)) needed.Add("DELETE");
            if (needed.Count > 0)
            {
                report.Add(DataMoveIssueSeverity.Refusal, "privileges.missing",
                    $"Role {roleName} lacks {string.Join(", ", needed)} on {table.DisplayName} in the {session.Role} database.");
            }

            if (isTarget && verb == DataMoveVerb.Copy && Flag(7) && !owner)
            {
                report.Add(DataMoveIssueSeverity.Refusal, "privileges.not-owner",
                    $"{table.DisplayName} in the {session.Role} database has user triggers (e.g. the row-version trigger) that copy must bypass to keep the stored values, which only its owner can do: connect as the table owner or a superuser.");
            }
        }
    }
}
