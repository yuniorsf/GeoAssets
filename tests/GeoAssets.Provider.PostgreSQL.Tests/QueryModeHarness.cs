using System.Security.Cryptography;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>Seeding, catalog construction and direct database access shared by the query-mode tests (XD01-449).</summary>
internal sealed class QueryModeHarness(PostgresContainerFixture fixture)
{
    /// <summary>An organization, a stranger and the map area and tokens one query-mode test seeds its features in.</summary>
    internal sealed record QueryWorld(
        Guid Org, Guid Other, string AssetType, double Lon, double Lat,
        string NameToken, string DescriptionToken, string KeyToken, string ValueToken);

    /// <summary>The organization's catalogs under test, with the shared catalog they sit on.</summary>
    internal sealed class QueryCatalogs(PostgresOrganizationAssetCatalogs catalogs, ValidatingAssetProvider shared) : IAsyncDisposable
    {
        public PostgresOrganizationAssetCatalogs Sut { get; } = catalogs;

        public IAssetProvider For(Guid organizationId) => Sut.For(organizationId);

        public async ValueTask DisposeAsync()
        {
            await Sut.DisposeAsync();
            await shared.DisposeAsync();
        }
    }

    internal sealed record QueryRowSnapshot(string Name, Guid OrganizationId);

    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>A fresh feature id within <c>geo_entity</c>'s 36 characters.</summary>
    public static string NewId(string prefix) => $"{prefix}-{Guid.NewGuid():N}"[..30];

    public static QueryWorld NewWorld() => new(
        Org: Guid.NewGuid(),
        Other: Guid.NewGuid(),
        AssetType: $"t-{Guid.NewGuid():N}",
        Lon: RandomNumberGenerator.GetInt32(-170, 170) + 0.5,
        Lat: RandomNumberGenerator.GetInt32(-80, 80) + 0.5,
        NameToken: $"nm{Guid.NewGuid():N}",
        DescriptionToken: $"ds{Guid.NewGuid():N}",
        KeyToken: $"ky{Guid.NewGuid():N}",
        ValueToken: $"vl{Guid.NewGuid():N}");

    /// <summary>A point feature; <paramref name="edges"/> are <c>(target, kind)</c> pairs.</summary>
    public static GeoFeature Feature(
        QueryWorld world, string id, Guid organizationId, double offset, string? assetType = null,
        params (string Target, string Kind)[] edges) => new()
    {
        Id = id,
        Geometry = new GeoPoint(world.Lon + offset, world.Lat + offset),
        Topology = [.. edges.Select(e => new TopoEdge { TargetId = e.Target, Kind = e.Kind })],
        Properties = new GeoFeatureProperties
        {
            Name = $"{world.NameToken} {id}",
            AssetTypeId = assetType ?? world.AssetType,
            OrganizationId = organizationId,
            CreatedAt = SeedTime,
            UpdatedAt = SeedTime,
            CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["plate"] = $"P-{id}" },
        },
    };

    public static GeoPolygon Around(QueryWorld world, double radius = 0.1) => new(
    [
        (world.Lon - radius, world.Lat - radius), (world.Lon + radius, world.Lat - radius),
        (world.Lon + radius, world.Lat + radius), (world.Lon - radius, world.Lat + radius),
        (world.Lon - radius, world.Lat - radius),
    ]);

    public static List<string> Ordinal(IEnumerable<string> ids) => [.. ids.OrderBy(id => id, StringComparer.Ordinal)];

    public static IEnumerable<string> Ids(IEnumerable<GeoFeature> features) => features.Select(f => f.Id);

    public async Task SeedAsync(params GeoFeature[] features)
    {
        await using var provider = NewDirectProvider();
        provider.AddRange(features);
    }

    /// <summary>Deletes the rows whatever their organization (a tenant-less provider sees only the shared ones).</summary>
    public async Task DeleteDirectAsync(params string[] ids)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.GeoEntities.IgnoreQueryFilters()
            .Where(row => ids.Contains(row.Id))
            .ExecuteDeleteAsync();
    }

    public async Task RenameDirectAsync(string id, string name)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        await db.GeoEntities.IgnoreQueryFilters()
            .Where(row => row.Id == id)
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.Name, name));
    }

    public async Task<QueryRowSnapshot?> RowAsync(string id)
    {
        await using var db = new GeoAssetsDbContext(fixture.CreateOptions());
        return await db.GeoEntities.IgnoreQueryFilters()
            .Where(row => row.Id == id)
            .Select(row => new QueryRowSnapshot(row.Name, row.OrganizationId))
            .SingleOrDefaultAsync();
    }

    /// <summary>The host's arrangement, after seeding: the warmed tenant-less provider as the shared catalog.</summary>
    public async Task<QueryCatalogs> NewCatalogsAsync(int fullCacheMaxFeatures, int enumerationLimit = 1_000, int byIdCacheCapacity = 100)
    {
        var factory = new PostgresProviderFactory(NullLoggerFactory.Instance, TimeProvider.System);
        var shared = new ValidatingAssetProvider(await factory.CreateAsync(fixture.ConnectionString));
        return new QueryCatalogs(
            new PostgresOrganizationAssetCatalogs(
                fixture.CreateOptions(), shared, NullLoggerFactory.Instance, TimeProvider.System,
                tileInvalidation: null,
                catalogOptions: new OrganizationCatalogOptions
                {
                    FullCacheMaxFeatures = fullCacheMaxFeatures,
                    EnumerationLimit = enumerationLimit,
                    ByIdCacheCapacity = byIdCacheCapacity,
                }),
            shared);
    }

    private PostgresAssetProvider NewDirectProvider()
    {
        var options = fixture.CreateOptions();
        return new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);
    }
}
