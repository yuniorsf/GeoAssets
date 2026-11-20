using FluentAssertions;
using GeoAssets.NeutralOperator.Server.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.NeutralOperator.Server.Tests.Migrations;

/// <summary>
/// The hand-written isolation SQL of this host's chain (<c>InitialNeutralOperatorIsolation.Isolation.cs</c>) claims to reproduce, for the
/// kernel's order tables, the final state the <c>GeoAssets.FieldService.Server</c> chain — the other isolated vertical database —
/// reaches migration by migration (XD01-382, AC4). This applies both chains to empty databases and compares, table for table
/// of this host's model: columns, indexes, constraints, row-level-security flags and policies, triggers, and the SQL functions the
/// policies and triggers call. The FieldService chain also carries vertical tables this host does not have; they are left out.
/// </summary>
public sealed class KernelTableParityTests
{
    private const string TablesSql =
        """SELECT table_name::text AS "Value" FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'""";

    private const string ColumnsSql =
        """
        SELECT (table_name || '|' || column_name || ':' || data_type || ':' || is_nullable || ':' || coalesce(character_maximum_length::text, '-')
                || ':' || coalesce(collation_name, '-') || ':' || coalesce(column_default, '-') || ':' || is_identity)::text AS "Value"
        FROM information_schema.columns WHERE table_schema = 'public'
        """;

    private const string IndexesSql =
        """SELECT (tablename || '|' || indexdef)::text AS "Value" FROM pg_indexes WHERE schemaname = 'public'""";

    private const string ConstraintsSql =
        """
        SELECT (replace(c.conrelid::regclass::text, '"', '') || '|' || c.conname || ' ' || pg_get_constraintdef(c.oid))::text AS "Value"
        FROM pg_constraint c WHERE c.connamespace = 'public'::regnamespace
        """;

    private const string RowSecuritySql =
        """
        SELECT (c.relname || '|' || c.relrowsecurity::text || ':' || c.relforcerowsecurity::text)::text AS "Value"
        FROM pg_class c WHERE c.relnamespace = 'public'::regnamespace AND c.relkind = 'r'
        """;

    private const string PoliciesSql =
        """
        SELECT (tablename || '|' || policyname || '|' || permissive || '|' || cmd || '|' || coalesce(qual, '-') || '|' || coalesce(with_check, '-'))::text AS "Value"
        FROM pg_policies WHERE schemaname = 'public'
        """;

    private const string TriggersSql =
        """
        SELECT (c.relname || '|' || t.tgname || '|' || pg_get_triggerdef(t.oid))::text AS "Value"
        FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid WHERE c.relnamespace = 'public'::regnamespace AND NOT t.tgisinternal
        """;

    private const string FunctionsSql =
        """
        SELECT (p.proname || '(' || pg_get_function_identity_arguments(p.oid) || ')|' || pg_get_functiondef(p.oid))::text AS "Value"
        FROM pg_proc p WHERE p.pronamespace = 'public'::regnamespace
        """;

    private static readonly Lazy<Task<(string Host, string Reference)>> Pair = new(MigratedPairAsync);

    private static async Task<(string Host, string Reference)> MigratedPairAsync()
    {
        var host = await NeutralOperatorDatabase.NewNameOnEmptyServerAsync();
        await using (var db = NeutralOperatorDatabase.ServiceOrderContext(host))
            await db.Database.MigrateAsync();

        var reference = await NeutralOperatorDatabase.NewNameOnEmptyServerAsync();
        await using (var db = NeutralOperatorDatabase.FieldServiceChainContext(reference))
            await db.Database.MigrateAsync();

        return (host, reference);
    }

    private static string KeyOf(string line) => line[..line.IndexOf('|', StringComparison.Ordinal)];

    /// <summary>
    /// The one difference in the kernel tables' columns: the FieldService history added <c>"OrderTypes"."RecipientAccess"</c> to
    /// a table that had rows, so EF gave the column <c>DEFAULT 0</c>; this host's chain creates the table fresh, and EF gives it
    /// none. Every write of an order type supplies the value, so the default is never read.
    /// </summary>
    private const string RecipientAccessColumn = "OrderTypes|RecipientAccess:";

    /// <summary>Compares what <paramref name="sql"/> reads in both databases, for the tables of this host (or, for functions, the names this host has).</summary>
    private static async Task AssertSameAsync(string description, string sql, bool functions = false, Func<string, bool>? except = null)
    {
        var (host, reference) = await Pair.Value;
        var actual = (await DatabaseProbe.ValuesAsync(host, sql)).Where(line => except?.Invoke(line) != true).ToList();
        var expected = (await DatabaseProbe.ValuesAsync(reference, sql)).Where(line => except?.Invoke(line) != true).ToList();
        var keys = functions
            ? actual.Select(KeyOf).ToHashSet(StringComparer.Ordinal)
            : (await DatabaseProbe.ValuesAsync(host, TablesSql)).ToHashSet(StringComparer.Ordinal);

        actual.Should().NotBeEmpty(description);
        actual.Should().BeEquivalentTo(
            expected.Where(line => keys.Contains(KeyOf(line))).ToList(),
            $"the {description} of this host's database must be what the FieldService chain ends in");
    }

    [Fact]
    public async Task TheHostsTables_ExistInTheReferenceToo_AsTheyAreTheKernelContextsTables()
    {
        var (host, reference) = await Pair.Value;

        (await DatabaseProbe.ValuesAsync(host, TablesSql)).Should().BeSubsetOf(await DatabaseProbe.ValuesAsync(reference, TablesSql));
    }

    [Fact]
    public Task Columns_AreTheFieldServiceChainsEndState_ExceptTheDefaultOfOrderTypesRecipientAccess() =>
        AssertSameAsync("columns", ColumnsSql, except: line => line.StartsWith(RecipientAccessColumn, StringComparison.Ordinal));

    [Fact]
    public async Task OrderTypesRecipientAccess_IsTheSameColumnInBothHistories_ApartFromTheDefaultOnlyTheOlderOneCarries()
    {
        var (host, reference) = await Pair.Value;

        (await DatabaseProbe.ValuesAsync(host, ColumnsSql)).Where(l => l.StartsWith(RecipientAccessColumn, StringComparison.Ordinal))
            .Should().Equal("OrderTypes|RecipientAccess:integer:NO:-:-:-:NO");
        (await DatabaseProbe.ValuesAsync(reference, ColumnsSql)).Where(l => l.StartsWith(RecipientAccessColumn, StringComparison.Ordinal))
            .Should().Equal("OrderTypes|RecipientAccess:integer:NO:-:-:0:NO");
    }

    [Fact]
    public Task Indexes_AreTheFieldServiceChainsEndState() => AssertSameAsync("indexes", IndexesSql);

    [Fact]
    public Task Constraints_AreTheFieldServiceChainsEndState() => AssertSameAsync("constraints", ConstraintsSql);

    [Fact]
    public Task RowLevelSecurityFlags_AreTheFieldServiceChainsEndState() => AssertSameAsync("row-level-security flags", RowSecuritySql);

    [Fact]
    public Task Policies_AreTheFieldServiceChainsEndState_DefinitionForDefinition() => AssertSameAsync("row-level-security policies", PoliciesSql);

    [Fact]
    public Task Triggers_AreTheFieldServiceChainsEndState_DefinitionForDefinition() => AssertSameAsync("triggers", TriggersSql);

    [Fact]
    public Task SqlFunctions_ThePoliciesAndTriggersCall_AreTheFieldServiceChainsEndState_DefinitionForDefinition() =>
        AssertSameAsync("SQL functions", FunctionsSql, functions: true);

    [Fact]
    public async Task SqlFunctions_TheHostCarriesNoSupersededOverload_TheFieldServiceHistoryLeftBehind()
    {
        var (host, reference) = await Pair.Value;
        const string Superseded = "service_order_recipient_action_is_granted(p_order_id character varying, p_action integer)|";

        (await DatabaseProbe.ValuesAsync(reference, FunctionsSql)).Should().Contain(l => l.StartsWith(Superseded, StringComparison.Ordinal));
        (await DatabaseProbe.ValuesAsync(host, FunctionsSql)).Should().NotContain(l => l.StartsWith(Superseded, StringComparison.Ordinal))
            .And.Contain(l => l.StartsWith("service_order_recipient_action_is_granted(p_order_id character varying, p_action integer, p_action_code character varying)|", StringComparison.Ordinal));
    }
}
