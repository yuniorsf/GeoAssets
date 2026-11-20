using FluentAssertions;
using GeoAssets.IpBilling.Persistence;
using Xunit;

namespace GeoAssets.IpBilling.EFCore.Tests;

/// <summary>The XD01-400 prune suite on SQLite (file-backed, <see cref="GrantSyncSqliteFixture"/>).</summary>
public sealed class EFMunicipioGrantSyncOutboxPruneSqliteTests : EFMunicipioGrantSyncOutboxPruneSuite
{
    protected override Task<IpBillingDbContext> CreateContextAsync() =>
        Task.FromResult(Own(new GrantSyncSqliteFixture()).NewContext());

    [Fact]
    public async Task Prune_OnAnEmptyOutbox_DeletesNothing() =>
        (await PruneEmptyOutboxAsync()).Should().Be(0);
}
