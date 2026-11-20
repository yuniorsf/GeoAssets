using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>
/// XD01-395: the <c>IpBillingMunicipioGrantSyncOutbox</c> migration of the real IpBilling chain, applied to an empty
/// PostgreSQL — up, down to the migration before it, up again — and the model snapshot it leaves (the probe: no pending
/// model changes).
/// </summary>
public sealed class IpBillingMunicipioGrantSyncOutboxMigrationTests
{
    private const string Previous = "20261116143857_IpBillingDistribuidoraKwhPosting";
    private const string Outbox = "20261120170600_IpBillingMunicipioGrantSyncOutbox";

    private const string TableExistsSql = "SELECT (to_regclass('public.\"MunicipioGrantSyncRequests\"') IS NOT NULL) AS \"Value\"";

    private const string ColumnsSql =
        """
        SELECT column_name::text || ':' || data_type::text || ':' || is_nullable::text AS "Value"
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'MunicipioGrantSyncRequests'
        ORDER BY ordinal_position
        """;

    private const string IndexesSql =
        """
        SELECT indexname::text AS "Value" FROM pg_indexes
        WHERE schemaname = 'public' AND tablename = 'MunicipioGrantSyncRequests' ORDER BY indexname
        """;

    private const string ForeignKeysSql =
        """
        SELECT conname::text AS "Value" FROM pg_constraint
        WHERE contype = 'f' AND conrelid = to_regclass('public."MunicipioGrantSyncRequests"')
        """;

    private const string DueIndexDefinitionSql =
        """
        SELECT indexdef::text AS "Value" FROM pg_indexes
        WHERE schemaname = 'public' AND tablename = 'MunicipioGrantSyncRequests' AND indexname = 'IX_MunicipioGrantSyncRequests_NextAttemptAt'
        """;

    [Fact]
    public async Task Up_CreatesTheOutboxTable_WithItsColumnsAndIndexes_AndNoForeignKeyToMunicipios()
    {
        await using var db = await IpBillingPostgres.NewContextOnNewDatabaseAsync();

        await db.Database.MigrateAsync();

        (await db.Database.GetAppliedMigrationsAsync()).Should().Contain(Outbox);
        (await db.Database.SqlQueryRaw<bool>(TableExistsSql).SingleAsync()).Should().BeTrue();
        (await db.Database.SqlQueryRaw<string>(ColumnsSql).ToListAsync()).Should().Equal(
            "Id:bigint:NO",
            "MunicipioId:uuid:NO",
            "Kind:character varying:NO",
            "IbgeCode:character varying:NO",
            "DistribuidoraOrganizationId:uuid:NO",
            "PrefeituraOrganizationId:uuid:YES",
            "PreviousPrefeituraOrganizationId:uuid:YES",
            "PreviousIbgeCode:character varying:YES",
            "PreviousDistribuidoraOrganizationId:uuid:YES",
            "RequestedBy:character varying:NO",
            "RequestedAt:timestamp with time zone:NO",
            "Attempts:integer:NO",
            "NextAttemptAt:timestamp with time zone:NO",
            "LeasedBy:character varying:YES",
            "LeaseExpiresAt:timestamp with time zone:YES",
            "LastError:character varying:YES",
            "CompletedAt:timestamp with time zone:YES",
            "FailedAt:timestamp with time zone:YES");
        (await db.Database.SqlQueryRaw<string>(IndexesSql).ToListAsync()).Should().Equal(
            "IX_MunicipioGrantSyncRequests_MunicipioId_Id",
            "IX_MunicipioGrantSyncRequests_NextAttemptAt",
            "PK_MunicipioGrantSyncRequests");
        (await db.Database.SqlQueryRaw<string>(ForeignKeysSql).ToListAsync()).Should().BeEmpty("a deleted município's revoke must outlive its row");
        (await db.Database.SqlQueryRaw<string>(DueIndexDefinitionSql).SingleAsync()).Should().Contain("WHERE").And.Contain("CompletedAt").And.Contain("FailedAt");
    }

    [Fact]
    public async Task Down_DropsTheOutboxTable_AndUpAgainRecreatesIt()
    {
        await using var db = await IpBillingPostgres.NewContextOnNewDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        await db.Database.MigrateAsync();

        await migrator.MigrateAsync(Previous);

        (await db.Database.SqlQueryRaw<bool>(TableExistsSql).SingleAsync()).Should().BeFalse();
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotContain(Outbox).And.Contain(Previous);

        await db.Database.MigrateAsync();

        (await db.Database.SqlQueryRaw<bool>(TableExistsSql).SingleAsync()).Should().BeTrue();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task TheModelSnapshot_HasNoPendingModelChanges()
    {
        await using var db = await IpBillingPostgres.NewContextOnNewDatabaseAsync();

        db.Database.HasPendingModelChanges().Should().BeFalse("the migration's snapshot matches the IpBillingDbContext model — the probe is empty");
        await Task.CompletedTask;
    }
}
