using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Server.Tiles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>
/// XD01-458 — per-scope tile generations, in one process: <see cref="TileVersions"/> keys the generation by organization
/// (plus the shared features' generation every scope includes), only ever adopting larger values; <see cref="TileInvalidation"/>
/// advances the writer's organization and its grantees only (the shared generation for an organization-less write, or when
/// the grantees can't be read); and <c>style.json</c> gives another organization the <c>{v}</c> it had.
/// </summary>
public sealed class TileScopeVersionTests
{
    private const string Style = "0123456789ab";
    private static readonly Guid OrgA = Guid.Parse("aaaaaaaa-0000-4000-8000-000000000458");
    private static readonly Guid OrgB = Guid.Parse("bbbbbbbb-0000-4000-8000-000000000458");
    private static readonly Guid OrgC = Guid.Parse("cccccccc-0000-4000-8000-000000000458");
    private static readonly AssetType Poles = new() { Id = Guid.Parse("00000000-0000-0000-0000-0000000004a8"), Name = "Poles", Color = "#123456", AllowedGeometryType = GeometryType.Point };
    private static readonly BoundingBox Change = new(-38.5005, -13.0005, -38.4995, -12.9995);

    // ── Versions ──────────────────────────────────────────────────────────────

    [Fact]
    public void AnOrganizationsGeneration_IsTheSharedOnePlusItsOwn()
    {
        var versions = new TileVersions("cafe0458");

        versions.Advance([OrgA]);
        versions.Advance([OrgA]);
        versions.Advance();

        versions.GenerationOf(OrgA).Should().Be(3);
        versions.GenerationOf(OrgB).Should().Be(1, "the shared features' write shows to every scope");
        versions.GenerationOf(Guid.Empty).Should().Be(1);
        versions.PublishedVersion(Style, OrgA).Should().Be("0123456789ab-cafe0458-0.3");
        versions.PublishedVersion(Style, OrgB).Should().Be("0123456789ab-cafe0458-0.1");
        versions.Generation.Should().Be(3, "every invalidation applied moves the drawing check");
    }

    [Fact]
    public void IsCurrent_ComparesWithTheCallersScope()
    {
        var versions = new TileVersions("cafe0458");
        versions.Advance([OrgA]);
        versions.Advance([OrgA]);

        versions.IsCurrent("0123456789ab-cafe0458-0.2", Style, OrgA).Should().BeTrue();
        versions.IsCurrent("0123456789ab-cafe0458-0.2", Style, OrgB).Should().BeFalse("B's scope is still at 0");
        versions.IsCurrent("0123456789ab-cafe0458-0.0", Style, OrgB).Should().BeTrue();
    }

    [Fact]
    public void Adopt_KeepsTheLargerValue_SoNoScopeGoesBackwards()
    {
        var versions = new TileVersions("cafe0458");
        versions.Adopt(new TileVersionSnapshot("beef0458", 4, new Dictionary<Guid, long> { [Guid.Empty] = 2, [OrgA] = 7 }));

        versions.Adopt(new Dictionary<Guid, long> { [OrgA] = 5, [OrgB] = 1 }, flushes: 3);
        versions.Adopt(new Dictionary<Guid, long> { [Guid.Empty] = 1 }, flushes: null);

        versions.Epoch.Should().Be("beef0458");
        versions.Flushes.Should().Be(4);
        versions.GenerationOf(OrgA).Should().Be(9);
        versions.GenerationOf(OrgB).Should().Be(3);
        versions.KnownScopes.Should().BeEquivalentTo([Guid.Empty, OrgA, OrgB]);
        versions.Knows(OrgC).Should().BeFalse();
    }

    // ── Invalidation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AnOrganizationsWrite_AdvancesItAndItsGrantees_Only()
    {
        var grants = new Grants();
        grants.ByGrantee[OrgB] = [Grant(grantee: OrgB, resource: OrgA)];
        var (cache, versions, invalidation) = Create(grants);
        var tile = TileAddress.Containing(-38.5, -13.0, 12);
        await cache.SetAsync(new TileCacheKey(TileCacheScope.Create("org-c", []), "assets", versions.CacheVersion(Style), tile, TileFormat.Mvt), Payload(), CancellationToken.None);

        await invalidation.InvalidateAsync([new DirtyTileRegion(Change)], OrgA, CancellationToken.None);

        versions.GenerationOf(OrgA).Should().Be(1);
        versions.GenerationOf(OrgB).Should().Be(1, "B holds a grant on A");
        versions.GenerationOf(OrgC).Should().Be(0);
        cache.Count.Should().Be(0, "a dirty tile is evicted for every scope, whatever scope's version moved");
    }

    [Fact]
    public async Task AnOrganizationLessWrite_AdvancesEveryScope()
    {
        var (_, versions, invalidation) = Create(new Grants());

        await invalidation.InvalidateAsync([new DirtyTileRegion(Change)], Guid.Empty, CancellationToken.None);
        await invalidation.InvalidateAsync([new DirtyTileRegion(Change)], CancellationToken.None);

        versions.GenerationOf(OrgA).Should().Be(2);
        versions.GenerationOf(OrgC).Should().Be(2);
    }

    [Fact]
    public async Task GranteesThatCantBeRead_AdvanceEveryScope()
    {
        var (_, versions, invalidation) = Create(new FailingGrants());

        await invalidation.InvalidateAsync([new DirtyTileRegion(Change)], OrgA, CancellationToken.None);

        versions.GenerationOf(OrgA).Should().Be(2);
        versions.GenerationOf(OrgC).Should().Be(1, "who sees A's features is unknown, so every scope's tiles may show them");
    }

    [Fact]
    public async Task AWriteTooLargeToEvict_FlushesAndAdvancesTheWritersOrganization()
    {
        var (_, versions, invalidation) = Create(new Grants(), maxEvicted: 100);

        await invalidation.InvalidateAsync([new DirtyTileRegion(new BoundingBox(-47.5, -24, -45, -22))], OrgA, CancellationToken.None);

        versions.Flushes.Should().Be(1);
        versions.GenerationOf(OrgA).Should().Be(1);
        versions.GenerationOf(OrgB).Should().Be(0);
    }

    // ── style.json ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AWriteInOrganizationA_LeavesOrganizationBsVersion()
    {
        await using var host = await TileTestHost.StartAsync([Poles]);
        using var a = host.ClientFor(OrgA);
        using var b = host.ClientFor(OrgB);
        var beforeA = await VersionAsync(a);
        var beforeB = await VersionAsync(b);

        await host.Invalidation.InvalidateAsync([new DirtyTileRegion(Change)], OrgA, CancellationToken.None);

        (await VersionAsync(a)).Should().NotBe(beforeA);
        (await VersionAsync(b)).Should().Be(beforeB);
        using var tile = await b.GetAsync(new Uri($"/tiles/assets/{beforeB}/{TileAddress.Containing(-38.5, -13.0, 12)}.mvt", UriKind.Relative));
        tile.StatusCode.Should().Be(HttpStatusCode.OK);
        tile.Headers.NonValidated["Cache-Control"].ToString().Should().Be(TileEndpointExtensions.ImmutableCacheControl, "B's {v} is still current");
    }

    [Fact]
    public async Task AWriteInOrganizationA_ChangesTheVersionOfAnOrganizationGrantedOnIt()
    {
        await using var host = await TileTestHost.StartAsync([Poles]);
        host.Grants.ByGrantee[OrgB] = [Grant(grantee: OrgB, resource: OrgA)];
        using var b = host.ClientFor(OrgB);
        using var c = host.ClientFor(OrgC);
        var beforeB = await VersionAsync(b);
        var beforeC = await VersionAsync(c);

        await host.Invalidation.InvalidateAsync([new DirtyTileRegion(Change)], OrgA, CancellationToken.None);

        (await VersionAsync(b)).Should().NotBe(beforeB);
        (await VersionAsync(c)).Should().Be(beforeC);
    }

    private static async Task<string> VersionAsync(HttpClient client)
    {
        var style = JsonNode.Parse(await client.GetStringAsync(new Uri("/tiles/style.json", UriKind.Relative)))!;
        return style["metadata"]!["geoassets:version"]!.GetValue<string>();
    }

    private static OrganizationGrant Grant(Guid grantee, Guid resource) =>
        new() { GranteeOrganizationId = grantee, ResourceOrganizationId = resource, AllowedActions = ["features:read"] };

    private static (MemoryTileCache Cache, TileVersions Versions, TileInvalidation Invalidation) Create(
        IOrganizationGrantRepository grants, int maxEvicted = TileServingOptions.DefaultMaxEvictedTilesPerWrite)
    {
        var services = new ServiceCollection().AddSingleton(grants).BuildServiceProvider();
        var cache = new MemoryTileCache();
        var versions = new TileVersions("cafe0458");
        var options = TileServingOptions.Default with { MaxEvictedTilesPerWrite = maxEvicted };
        return (cache, versions, new TileInvalidation(cache, versions, options, services.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System, sync: null));
    }

    private static TilePayload Payload() => new(new byte[] { 1, 2, 3 }, TileFormats.MapboxVectorTileContentType);

    private sealed class FailingGrants : IOrganizationGrantRepository
    {
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsIssuedByAsync(Guid resourceOrganizationId, DateTime utcNow, CancellationToken ct = default) =>
            throw new InvalidOperationException("The identity database is down.");

        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrganizationGrant?> FindActiveGrantByKeyAsync(OrganizationGrantKey key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
