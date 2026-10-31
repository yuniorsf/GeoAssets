using FluentAssertions;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Entities;
using GeoAssets.Provider.PostgreSQL.Tests.TestDoubles;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests.Data;

/// <summary>
/// Exercises <see cref="GeoAssetsDbContext"/>'s EF Core global query filter on
/// <see cref="GeoEntityRow.OrganizationId"/> — the application-layer half of tenant row
/// isolation. The database-layer half (native Row-Level Security) is covered separately by
/// <see cref="GeoEntityRowLevelSecurityPolicyTests"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeoAssetsDbContextTenantIsolationTests(PostgresContainerFixture fixture)
{
    private static readonly Guid OrgA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OrgB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private async Task ClearAsync()
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE geo_entity CASCADE");
    }

    private async Task SeedAsync(string sql)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.Database.ExecuteSqlRawAsync(sql);
    }

    [Fact]
    public async Task GeoEntities_ScopedToOrgA_ReturnsOnlyOrgARow()
    {
        await ClearAsync();
        await SeedAsync($"""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "OrganizationId")
            VALUES
                ('a', 'Asset A', 'type-a', '{OrgA}'),
                ('b', 'Asset B', 'type-a', '{OrgB}');
            """);

        await using var sut = new GeoAssetsDbContext(fixture.CreateOptions(), new FakeTenantContext(OrgA));

        var rows = await sut.GeoEntities.ToListAsync();

        rows.Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public async Task GeoEntities_ScopedToOrgB_ReturnsOnlyOrgBRow()
    {
        await ClearAsync();
        await SeedAsync($"""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "OrganizationId")
            VALUES
                ('a', 'Asset A', 'type-a', '{OrgA}'),
                ('b', 'Asset B', 'type-a', '{OrgB}');
            """);

        await using var sut = new GeoAssetsDbContext(fixture.CreateOptions(), new FakeTenantContext(OrgB));

        var rows = await sut.GeoEntities.ToListAsync();

        rows.Should().ContainSingle().Which.Id.Should().Be("b");
    }

    [Fact]
    public async Task GeoEntities_NoTenantContextSupplied_SeesOnlyGuidEmptySentinelRowsUnchangedFromPreTenancyBehavior()
    {
        await ClearAsync();
        await SeedAsync($"""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "OrganizationId")
            VALUES
                ('a', 'Asset A', 'type-a', '{OrgA}'),
                ('b', 'Asset B', 'type-a', '{OrgB}'),
                ('legacy', 'Legacy Asset', 'type-a', '{Guid.Empty}');
            """);

        await using var sut = new GeoAssetsDbContext(fixture.CreateOptions());

        var rows = await sut.GeoEntities.ToListAsync();

        rows.Should().ContainSingle().Which.Id.Should().Be("legacy");
    }

    [Fact]
    public async Task SaveChangesAsync_AttachedRowBelongsToAnotherOrganization_UpdatesByPrimaryKeyRegardlessOfQueryFilter()
    {
        await ClearAsync();
        await SeedAsync($"""
            INSERT INTO geo_entity ("Id", "Name", "AssetTypeId", "OrganizationId")
            VALUES ('b', 'Original Name', 'type-a', '{OrgB}');
            """);

        // A GeoAssetsDbContext constructed with no ITenantContext still applies the query filter
        // (scoped to Guid.Empty, via NullTenantContext), so fetching orgB's row unfiltered needs
        // IgnoreQueryFilters() — not just "no tenant context supplied".
        string orgBRowId;
        await using (var unfiltered = new GeoAssetsDbContext(fixture.CreateOptions()))
        {
            var orgBRow = await unfiltered.GeoEntities.IgnoreQueryFilters().SingleAsync(e => e.Id == "b");
            orgBRowId = orgBRow.Id;
        }

        await using var sut = new GeoAssetsDbContext(fixture.CreateOptions(), new FakeTenantContext(OrgA));
        var attachedRow = new GeoEntityRow
        {
            Id = orgBRowId,
            Name = "Hijacked From OrgA",
            AssetTypeId = "type-a",
            OrganizationId = OrgB,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        };
        sut.GeoEntities.Attach(attachedRow);
        sut.Entry(attachedRow).State = EntityState.Modified;

        // GeoEntityRow has no concurrency token, so SaveChangesAsync generates a plain
        // "UPDATE geo_entity SET ... WHERE Id = @id" — the global query filter is a LINQ-query
        // concern and is never consulted for an entity attached directly to the change tracker.
        // This is exactly why the RLS policy in GeoEntityRowLevelSecurityPolicyTests exists as a
        // defense-in-depth layer: the EF query filter alone does not stop a cross-tenant write
        // issued this way.
        await sut.SaveChangesAsync();

        await using var verify = new GeoAssetsDbContext(fixture.CreateOptions());
        var reread = await verify.GeoEntities.IgnoreQueryFilters().SingleAsync(e => e.Id == "b");
        reread.Name.Should().Be("Hijacked From OrgA");
    }
}
