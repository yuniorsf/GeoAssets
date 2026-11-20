using FluentAssertions;
using GeoAssets.IpBilling.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>The XD01-400 prune suite on PostgreSQL (the migrated schema, Testcontainers).</summary>
public sealed class EFMunicipioGrantSyncOutboxPrunePostgresTests : EFMunicipioGrantSyncOutboxPruneSuite
{
    protected override async Task<IpBillingDbContext> CreateContextAsync()
    {
        var db = await IpBillingPostgres.NewContextOnNewDatabaseAsync();
        await db.Database.MigrateAsync();
        return db;
    }

    [Fact]
    public async Task Prune_OnAnEmptyOutbox_DeletesNothing() =>
        (await PruneEmptyOutboxAsync()).Should().Be(0);
}
