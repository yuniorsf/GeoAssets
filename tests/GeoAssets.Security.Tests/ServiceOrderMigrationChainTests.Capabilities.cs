using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace GeoAssets.Security.Tests;

/// <summary>
/// The XD01-361 migration <c>AddOrderTypeCapabilities</c> in the <c>GeoAssets.Server</c> history: the
/// <c>"CapabilitiesJson"</c> column, and the backfill that keeps the shared-use types committing occupancy.
/// </summary>
public partial class ServiceOrderMigrationChainTests
{
    private const string CapabilitiesMigration = "AddOrderTypeCapabilities";

    [Fact]
    public async Task AddOrderTypeCapabilitiesMigration_BackfillsOnlyTheSharedUseTypes()
    {
        await using var db = await NewContextOnEmptyDatabaseAsync();
        var migrator = db.GetService<IMigrator>();
        var chain = db.Database.GetMigrations().ToList();
        var migration = MigrationId(db, CapabilitiesMigration);
        var previous = chain[chain.IndexOf(migration) - 1];

        await migrator.MigrateAsync(previous);
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO "OrderTypes" ("Id", "DisplayName", "Description", "TargetPolicyJson") VALUES
                ('shared-use-pole-work', 'Pole work', '', '{{"targetAssetTypeIds":["pole"]}}'),
                ('shared-use-occupant-notice', 'Notice', '', NULL),
                ('custom-survey', 'Survey', '', '{{"targetAssetTypeIds":["pole"]}}')
            """);

        await migrator.MigrateAsync(migration);

        (await QueryAsync(db,
            """SELECT ("Id" || ':' || coalesce("CapabilitiesJson", '-'))::text AS "Value" FROM "OrderTypes" ORDER BY "Id" COLLATE "C" """))
            .Should().Equal("custom-survey:-", "shared-use-occupant-notice:-", """shared-use-pole-work:["occupancy-commit"]""");

        await migrator.MigrateAsync(previous);
        (await QueryAsync(db, """SELECT column_name::text AS "Value" FROM information_schema.columns WHERE table_name = 'OrderTypes' AND column_name = 'CapabilitiesJson'"""))
            .Should().BeEmpty();
    }
}
