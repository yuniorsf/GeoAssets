using FluentAssertions;
using GeoAssets.Tools.NeutralOperatorDataMove.Tests.Support;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests;

/// <summary>What the tool refuses or reports as a conflict — and that, when it does, it has written nothing.</summary>
public sealed class RefusalTests
{
    private const string Org = "a0a0a0a0-0000-4000-8000-000000000382";

    private static string OrderInsert(string id, string title, string orderType = "shared-use-pole-work", string? parent = null) => $$"""
        INSERT INTO "ServiceOrders" ("Id","Title","Description","OrderTypeId","Status","Priority","CreatedBy","OrganizationId","CreatedAt","AttributesJson","FeatureIdsJson","RowVersion","ParentOrderId")
        VALUES ('{{id}}','{{title}}','d','{{orderType}}','Draft',1,'owner-admin','{{Org}}','2026-11-20T10:00:00Z','{}','[]','\x00',{{(parent is null ? "NULL" : $"'{parent}'")}})
        """;

    // ── conflicts ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Copy_WhenTheTargetHoldsADifferentRowWithTheSameId_ExitsTwo_AndWritesNothing()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();
        await Sql.ExecAsync(target, OrderInsert("no-created", "A different order the target made itself"));
        var kernelBefore = await DatabaseState.CaptureAsync(kernel);
        var targetBefore = await DatabaseState.CaptureAsync(target);

        var result = await MoveTool.RunAsync(kernel, target, "copy", "--include-pending-outbox");

        result.ExitCode.Should().Be(DataMoveExitCodes.DifferencesFound, result.Text);
        result.Text.Should().Contain("differs: no-created").And.Contain("conflicts or differences found; nothing was overwritten");
        ReportRows.Of(result.Text, "public.ServiceOrders")[ReportRows.Differ].Should().Be(1);
        ReportRows.Total(result.Text, ReportRows.Copied).Should().Be(0);
        (await DatabaseState.CaptureAsync(target)).Differences(targetBefore).Should().BeEmpty("a conflicting copy writes nothing at all, not even the rows that did not conflict");
        (await DatabaseState.CaptureAsync(kernel)).Differences(kernelBefore).Should().BeEmpty();
        (await Sql.ScalarAsync(target, """SELECT "Title" FROM public."ServiceOrders" WHERE "Id" = 'no-created'""")).Should().Be("A different order the target made itself", "the target's row is never overwritten");
    }

    [Fact]
    public async Task Verify_AfterACopyWhoseTargetRowChanged_ExitsTwo()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();
        await MoveTool.RunAsync(kernel, target, "copy", "--include-pending-outbox");
        await Sql.ExecAsync(target, """UPDATE public."ServiceOrders" SET "Title" = 'edited' WHERE "Id" = 'no-staged'""");

        var result = await MoveTool.RunAsync(kernel, target, "verify");

        result.ExitCode.Should().Be(2, result.Text);
        result.Text.Should().Contain("differs: no-staged");
    }

    [Fact]
    public async Task Verify_BeforeTheCopy_ExitsTwo_ReportingEveryRowMissing()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();

        var result = await MoveTool.RunAsync(kernel, target, "verify");

        result.ExitCode.Should().Be(2, result.Text);
        ReportRows.Of(result.Text, "public.ServiceOrders")[ReportRows.Missing].Should().Be(6);
    }

    [Fact]
    public async Task Plan_WhenAMovedOrdersParentIsNeitherMovedNorInTheTarget_IsAConflict()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();
        await Sql.ExecAsync(kernel, OrderInsert("kernel-parent", "A kernel parent", "inspection"));
        await Sql.ExecAsync(kernel, OrderInsert("no-orphan", "Orphaned child", parent: "kernel-parent"));

        var plan = await MoveTool.RunAsync(kernel, target, "plan");
        var copy = await MoveTool.RunAsync(kernel, target, "copy", "--include-pending-outbox");

        plan.ExitCode.Should().Be(2, plan.Text);
        plan.Text.Should().Contain("order.parent-not-moved").And.Contain("kernel-parent");
        copy.ExitCode.Should().Be(2, copy.Text);
        (await Sql.CountAsync(target, "public.\"ServiceOrders\"")).Should().Be(0, "nothing is copied while an order's parent cannot follow it");
    }

    [Fact]
    public async Task Plan_WhenTheKernelsLedgerAlreadyHoldsAMovedOrderUnderTheVertical_IsAConflict()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();
        await Sql.ExecAsync(kernel, $$"""
            INSERT INTO "ServiceOrderTargets" ("Id","ServiceOrderId","VerticalId","FeatureId","AssetTypeId","ActivityIdsJson","Status","ConflictLevel","AddedAt","AddedBy","OrganizationId","Version")
            VALUES ('t-keyed','no-created','neutral-operator','pole-k','pole','[]','Pending',0,'2026-11-20T10:00:00Z','svc','{{Org}}',1)
            """);

        var plan = await MoveTool.RunAsync(kernel, target, "plan");

        plan.ExitCode.Should().Be(2, plan.Text);
        plan.Text.Should().Contain("ledger.already-keyed");
    }

    // ── refusals: preconditions ───────────────────────────────────────────

    public static TheoryData<string, string, string> NotAtHead => new()
    {
        { "kernel", """DELETE FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261122224401_AddCrossOrganizationSyncMirrors'""", "migrations.service-orders" },
        { "target", """DELETE FROM public."__EFMigrationsHistory" WHERE "MigrationId" = '20261122224327_AddCrossOrganizationSyncMirrors'""", "migrations.service-orders" },
        { "target", """DELETE FROM neutral_operator."__EFMigrationsHistory_neutral_operator" WHERE "MigrationId" = (SELECT max("MigrationId") FROM neutral_operator."__EFMigrationsHistory_neutral_operator")""", "migrations.neutral-operator" },
        { "kernel", """INSERT INTO neutral_operator."__EFMigrationsHistory_neutral_operator" ("MigrationId","ProductVersion") VALUES ('29991231000000_FromTheFuture','10.0.7')""", "migrations.neutral-operator" },
    };

    [Theory]
    [MemberData(nameof(NotAtHead))]
    public async Task Plan_WhenADatabaseIsNotAtTheMigrationHead_Refuses_NamingTheChain(string database, string sql, string code)
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();
        await Sql.ExecAsync(string.Equals(database, "kernel", StringComparison.Ordinal) ? kernel : target, sql);

        foreach (var verb in new[] { "plan", "copy", "verify" })
        {
            var result = await MoveTool.RunAsync(kernel, target, [verb, .. FlagsOf(verb)]);
            result.ExitCode.Should().Be(3, $"{verb}: {result.Text}");
            result.Text.Should().Contain(code).And.Contain("refused; nothing was changed");
        }

        (await Sql.CountAsync(target, "public.\"ServiceOrders\"")).Should().Be(0);
        var remove = await MoveTool.RunAsync(kernel, target, "remove", "--confirm");
        remove.ExitCode.Should().Be(3, remove.Text);
        (await Sql.CountAsync(kernel, "public.\"ServiceOrders\"")).Should().Be(7, "a refused remove deletes nothing");
    }

    [Fact]
    public async Task Plan_WhenTheKernelHasNoMigrationHistoryAtAll_RefusesToApplyTheChainsFirst()
    {
        var (_, target) = await MoveTool.SeededPairAsync();
        var admin = await MoveDatabases.AdminAsync();
        var empty = MoveDatabases.WithDatabase(admin, "postgres");

        var result = await MoveTool.RunAsync(empty, target, "plan");

        result.ExitCode.Should().Be(3, result.Text);
        result.Text.Should().Contain("migrations.missing");
    }

    public static TheoryData<string, string, string> Drift => new()
    {
        { "target", """ALTER TABLE neutral_operator.field_pole_occupancies ADD COLUMN "Extra" text""", "schema.column-drift" },
        { "kernel", """ALTER TABLE public."ServiceOrders" ADD COLUMN "Surprise" integer""", "schema.column-drift" },
        { "target", """ALTER TABLE public."OrderActionLogs" DROP COLUMN "Comment" """, "schema.column-drift" },
        { "kernel", """ALTER TABLE public."ServiceOrders" ALTER COLUMN "Priority" TYPE text USING "Priority"::text""", "schema.type-mismatch" },
        { "target", """CREATE TABLE neutral_operator.stray_table (id integer)""", "schema.unknown-table" },
        { "target", """DROP TABLE neutral_operator.occupancy_apply_saga_poles""", "schema.table-missing" },
    };

    [Theory]
    [MemberData(nameof(Drift))]
    public async Task Copy_WhenTheSchemaDriftedFromTheModel_Refuses_AndWritesNothing(string database, string sql, string code)
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();
        await Sql.ExecAsync(string.Equals(database, "kernel", StringComparison.Ordinal) ? kernel : target, sql);
        var targetBefore = await Sql.CountAsync(target, "public.\"ServiceOrders\"");

        var result = await MoveTool.RunAsync(kernel, target, "copy", "--include-pending-outbox");

        result.ExitCode.Should().Be(3, result.Text);
        result.Text.Should().Contain(code);
        (await Sql.CountAsync(target, "public.\"ServiceOrders\"")).Should().Be(targetBefore);
    }

    [Fact]
    public async Task Plan_WithARoleRowLevelSecurityFilters_Refuses_ForEveryVerb_AndNeverTheOwner()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();
        var role = $"mover_{Guid.NewGuid():N}";
        await GrantReadAsync(role, "NOBYPASSRLS", kernel, target);
        var filtered = (Kernel: MoveDatabases.WithRole(kernel, role, "pw"), Target: MoveDatabases.WithRole(target, role, "pw"));

        foreach (var verb in new[] { "plan", "copy", "verify" })
        {
            var result = await MoveTool.RunAsync(filtered.Kernel, filtered.Target, [verb, .. FlagsOf(verb)]);
            result.ExitCode.Should().Be(3, $"{verb}: {result.Text}");
            result.Text.Should().Contain("rls.filtered").And.Contain("BYPASSRLS");
            result.Text.Should().NotContain("pw", "no credentials in the report");
        }

        (await Sql.CountAsync(target, "public.\"ServiceOrders\"")).Should().Be(0);
        (await MoveTool.RunAsync(kernel, target, "plan")).ExitCode.Should().Be(0, "the tables' owner is not filtered");
    }

    [Fact]
    public async Task Plan_WithARoleThatBypassesRowLevelSecurity_IsNotRefused()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();
        var role = $"bypass_{Guid.NewGuid():N}";
        await GrantReadAsync(role, "BYPASSRLS", kernel, target);

        var result = await MoveTool.RunAsync(MoveDatabases.WithRole(kernel, role, "pw"), MoveDatabases.WithRole(target, role, "pw"), "plan");

        result.ExitCode.Should().Be(0, result.Text);
        ReportRows.Of(result.Text, "public.ServiceOrders")[ReportRows.Source].Should().Be(6, "the role sees every row");
    }

    [Fact]
    public async Task Copy_WithARoleThatCannotWriteTheTarget_Refuses_NamingThePrivilege()
    {
        var (kernel, target) = await MoveTool.SeededPairAsync();
        var role = $"reader_{Guid.NewGuid():N}";
        await GrantReadAsync(role, "BYPASSRLS", kernel, target);

        var result = await MoveTool.RunAsync(MoveDatabases.WithRole(kernel, role, "pw"), MoveDatabases.WithRole(target, role, "pw"), "copy", "--include-pending-outbox");

        result.ExitCode.Should().Be(3, result.Text);
        result.Text.Should().Contain("privileges.missing").And.Contain("INSERT");
        (await Sql.CountAsync(target, "public.\"ServiceOrders\"")).Should().Be(0);
    }

    [Fact]
    public async Task AnyVerb_WithTheSameDatabaseForSourceAndTarget_IsRefusedBeforeConnecting()
    {
        var (kernel, _) = await MoveTool.SeededPairAsync();
        var sameDatabaseOtherCredentials = MoveDatabases.WithRole(kernel, "someone-else", "other-pw");

        foreach (var verb in new[] { "plan", "copy", "verify" })
        {
            var result = await MoveTool.RunAsync(kernel, sameDatabaseOtherCredentials, verb);
            result.ExitCode.Should().Be(DataMoveExitCodes.UsageError, result.Text);
            result.Error.Should().Contain("name the same database").And.NotContain("other-pw");
        }

        var remove = await MoveTool.RunAsync(kernel, kernel, "remove", "--confirm");
        remove.ExitCode.Should().Be(1);
        (await Sql.CountAsync(kernel, "public.\"ServiceOrders\"")).Should().Be(7);
    }

    [Fact]
    public async Task Plan_WhenADatabaseIsUnreachable_ExitsFour_WithoutLeakingTheConnectionString()
    {
        var (kernel, _) = await MoveTool.SeededPairAsync();
        const string unreachable = "Host=127.0.0.1;Port=1;Database=nowhere;Username=nobody;Password=leaky-password-123;Timeout=2;Pooling=false";

        var result = await MoveTool.RunAsync(kernel, unreachable, "plan");

        result.ExitCode.Should().Be(DataMoveExitCodes.Failed, result.Text);
        result.Text.Should().Contain("Failed:").And.Contain("rolled back");
        result.Text.Should().NotContain("leaky-password-123").And.NotContain("nobody");
    }

    /// <summary>The pending-outbox flag belongs to plan and copy only.</summary>
    private static string[] FlagsOf(string verb) => string.Equals(verb, "verify", StringComparison.Ordinal) ? [] : ["--include-pending-outbox"];

    private static async Task GrantReadAsync(string role, string bypass, params string[] databases)
    {
        await Sql.ExecAsync(await MoveDatabases.AdminAsync(), $"""CREATE ROLE "{role}" LOGIN PASSWORD 'pw' {bypass}""");
        foreach (var database in databases)
        {
            await Sql.ExecAsync(database, $"""
                GRANT USAGE ON SCHEMA public, neutral_operator TO "{role}";
                GRANT SELECT ON ALL TABLES IN SCHEMA public TO "{role}";
                GRANT SELECT ON ALL TABLES IN SCHEMA neutral_operator TO "{role}";
                """);
        }
    }
}
