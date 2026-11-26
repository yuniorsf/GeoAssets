using System.Security.Cryptography;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// Data for the vector tile tests (XD01-432), all near the center of one random zoom-<see cref="Zoom"/> tile
/// (<see cref="Tile"/>) so no feature straddles its edge: organizations A and B and the shared owner; a pole type whose
/// layer rule reads <c>status</c>, a line type, and a type drawn from zoom <see cref="LateMinZoom"/> on.
/// <see cref="DisposeAsync"/> removes exactly the rows this world inserted.
/// </summary>
internal sealed class VectorTileWorld : IAsyncDisposable
{
    public const int Zoom = 14;

    public const int LateMinZoom = 15;

    private readonly PostgresContainerFixture _fixture;
    private readonly List<string> _featureIds = [];
    private readonly List<Guid> _assetTypeIds = [];
    private readonly List<Guid> _layerIds = [];
    private readonly List<Guid> _layerRuleIds = [];

    private VectorTileWorld(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
        Tile = TileAddress.Containing(RandomNumberGenerator.GetInt32(-170, 170) + 0.5, RandomNumberGenerator.GetInt32(-60, 60) + 0.5, Zoom);
        var box = Tile.Envelope4326;
        CenterLon = (box.MinLon + box.MaxLon) / 2;
        CenterLat = (box.MinLat + box.MaxLat) / 2;
        Step = (box.MaxLat - box.MinLat) / 20;
    }

    public TileAddress Tile { get; }

    public double CenterLon { get; }

    public double CenterLat { get; }

    /// <summary>A twentieth of the tile's height: features sit a few steps from its center.</summary>
    public double Step { get; }

    public Guid OrgA { get; } = Guid.NewGuid();

    public Guid OrgB { get; } = Guid.NewGuid();

    public Guid OverrideLayerId { get; private set; }

    public string Poles { get; private set; } = string.Empty;

    public string Lines { get; private set; } = string.Empty;

    public string Late { get; private set; } = string.Empty;

    public string PoleA { get; private set; } = string.Empty;

    public string PoleA2 { get; private set; } = string.Empty;

    public string LineA { get; private set; } = string.Empty;

    public string LateA { get; private set; } = string.Empty;

    public string SharedPole { get; private set; } = string.Empty;

    public string PoleB { get; private set; } = string.Empty;

    public string FarPoleA { get; private set; } = string.Empty;

    /// <summary>The longitude/latitude of <see cref="PoleA"/>.</summary>
    public (double Lon, double Lat) PoleAPosition => (CenterLon + Step, CenterLat + Step);

    /// <summary>The longitude/latitude of <see cref="LateA"/>.</summary>
    public (double Lon, double Lat) LateAPosition => (CenterLon - (2 * Step), CenterLat + (2 * Step));

    public static async Task<VectorTileWorld> SeedAsync(PostgresContainerFixture fixture)
    {
        var world = new VectorTileWorld(fixture);
        await world.SeedRowsAsync();
        return world;
    }

    public TileRequest Request(params string[] layers) => new(new TileLayerSet("test", layers), Tile);

    public static TileRequest Request(TileAddress address, params string[] layers) => new(new TileLayerSet("test", layers), address);

    private string NewId(string prefix)
    {
        var id = $"{prefix}-{Guid.NewGuid():N}"[..30];
        _featureIds.Add(id);
        return id;
    }

    private static GeoFeature Feature(
        string id, string type, Guid organizationId, GeoGeometry geometry, string? layerId = null, params (string Key, string Value)[] attributes) => new()
    {
        Id = id,
        Geometry = geometry,
        Properties = new GeoFeatureProperties
        {
            Name = $"name {id}",
            AssetTypeId = type,
            LayerId = layerId ?? string.Empty,
            OrganizationId = organizationId,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            CustomAttributes = attributes.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal),
        },
    };

    private async Task SeedRowsAsync()
    {
        var options = _fixture.CreateOptions();
        await using var provider = new PostgresAssetProvider(
            new GeoAssetsDbContext(options), options, NullLogger<PostgresAssetProvider>.Instance, TimeProvider.System);

        var ruleLayer = NewLayer(provider);
        var overrideLayer = NewLayer(provider);
        OverrideLayerId = overrideLayer.Id;
        Poles = NewType(provider, "poles", null);
        Lines = NewType(provider, "lines", null);
        Late = NewType(provider, "late", LateMinZoom);

        var rule = new LayerRule
        {
            AssetTypeId = Guid.Parse(Poles),
            LayerId = ruleLayer.Id,
            Priority = 1,
            Conditions = [new LayerRuleCondition { Attribute = "status", Operator = LayerRuleOperator.Equals, Value = "ok" }],
        };
        _layerRuleIds.Add(rule.Id);
        provider.AddLayerRule(rule);

        // A rule reading an attribute named like a property every feature carries: the property wins.
        var shadowing = new LayerRule
        {
            AssetTypeId = Guid.Parse(Poles),
            LayerId = ruleLayer.Id,
            Priority = 2,
            Conditions = [new LayerRuleCondition { Attribute = "name", Operator = LayerRuleOperator.Equals, Value = "spoofed" }],
        };
        _layerRuleIds.Add(shadowing.Id);
        provider.AddLayerRule(shadowing);

        PoleA = NewId("pole-a");
        PoleA2 = NewId("pole-a2");
        LineA = NewId("line-a");
        LateA = NewId("late-a");
        SharedPole = NewId("shared");
        PoleB = NewId("pole-b");
        FarPoleA = NewId("far-a");

        provider.AddRange(
        [
            Feature(PoleA, Poles, OrgA, new GeoPoint(PoleAPosition.Lon, PoleAPosition.Lat), overrideLayer.Id.ToString(),
                ("status", "ok"), ("other", "x"), ("name", "spoofed")),
            Feature(PoleA2, Poles, OrgA, new GeoPoint(CenterLon - Step, CenterLat - Step), null, ("status", "bad")),
            Feature(LineA, Lines, OrgA, new GeoLineString([(CenterLon - (3 * Step), CenterLat), (CenterLon + (3 * Step), CenterLat)]), null, ("status", "ok")),
            Feature(LateA, Late, OrgA, new GeoPoint(LateAPosition.Lon, LateAPosition.Lat)),
            Feature(SharedPole, Poles, Guid.Empty, new GeoPoint(CenterLon + (2 * Step), CenterLat - (2 * Step))),
            Feature(PoleB, Poles, OrgB, new GeoPoint(CenterLon, CenterLat + (3 * Step))),
            Feature(FarPoleA, Poles, OrgA, new GeoPoint(CenterLon + 1, CenterLat)),
        ]);
    }

    private Layer NewLayer(PostgresAssetProvider provider)
    {
        var layer = new Layer { Name = $"tiles-{Guid.NewGuid():N}", GeometryType = GeometryType.Point, Color = "#123456" };
        _layerIds.Add(layer.Id);
        provider.AddLayer(layer);
        return layer;
    }

    private string NewType(PostgresAssetProvider provider, string name, int? minZoom)
    {
        var type = new AssetType { Name = $"tiles-{name}-{Guid.NewGuid():N}", Color = "#654321", MinZoom = minZoom };
        _assetTypeIds.Add(type.Id);
        provider.AddAssetType(type);
        return type.Id.ToString();
    }

    public async ValueTask DisposeAsync()
    {
        await using var db = new GeoAssetsDbContext(_fixture.CreateOptions());
        await db.GeoEntities.IgnoreQueryFilters().Where(e => _featureIds.Contains(e.Id)).ExecuteDeleteAsync();
        await db.LayerRules.Where(r => _layerRuleIds.Contains(r.Id)).ExecuteDeleteAsync();
        await db.AssetTypes.Where(t => _assetTypeIds.Contains(t.Id)).ExecuteDeleteAsync();
        await db.Layers.Where(l => _layerIds.Contains(l.Id)).ExecuteDeleteAsync();
    }
}
