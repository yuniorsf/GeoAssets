using System.Security.Cryptography;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Data;
using GeoAssets.Provider.PostgreSQL.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// Data for the render-query tests (XD01-372): two organizations and a shared (organization-less) owner, features of
/// several asset types covering each tier of layer resolution (feature override, matching layer rule, the type's
/// default layer, no layer), all inside one random 0.2° square so tests never collide. <see cref="DisposeAsync"/>
/// removes exactly the rows this world inserted.
/// </summary>
internal sealed class RenderQueryWorld : IAsyncDisposable
{
    public const string OverrideColor = "#ff0001";
    public const string RuleColor = "#00ff02";
    public const string DefaultLayerColor = "#0000ff";
    public const string PlainTypeColor = "#123456";
    public const string RuledTypeColor = "#654321";

    private const double PlainOffset = 0.005;

    /// <summary>The default-layer type is drawn up to this zoom (XD01-430); the ruled type from <see cref="RuledMinZoom"/>; the plain type at every zoom.</summary>
    public const int DefaultLayerMaxZoom = 10;

    /// <summary>The ruled type is drawn from this zoom on (XD01-430).</summary>
    public const int RuledMinZoom = 14;

    private readonly PostgresContainerFixture _fixture;
    private readonly List<string> _featureIds = [];
    private readonly List<Guid> _assetTypeIds = [];
    private readonly List<Guid> _layerIds = [];
    private readonly List<Guid> _layerRuleIds = [];
    private PostgresOrganizationAssetCatalogs? _catalogs;
    private ValidatingAssetProvider? _shared;

    private RenderQueryWorld(PostgresContainerFixture fixture)
    {
        _fixture = fixture;
        Lon = RandomNumberGenerator.GetInt32(-170, 170) + 0.5;
        Lat = RandomNumberGenerator.GetInt32(-80, 80) + 0.5;
    }

    public Guid OrgA { get; } = Guid.NewGuid();
    public Guid OrgB { get; } = Guid.NewGuid();
    public double Lon { get; }
    public double Lat { get; }

    public AssetType DefaultLayerType { get; private set; } = null!;
    public AssetType RuledType { get; private set; } = null!;
    public AssetType PlainType { get; private set; } = null!;

    public string OverrideId { get; private set; } = string.Empty;
    public string RuleId { get; private set; } = string.Empty;
    public string RuledNoMatchId { get; private set; } = string.Empty;
    public string DefaultLayerId { get; private set; } = string.Empty;
    public string PlainId { get; private set; } = string.Empty;
    public string SharedId { get; private set; } = string.Empty;
    public string OrgBId { get; private set; } = string.Empty;
    public string GeometrylessId { get; private set; } = string.Empty;

    /// <summary>Every feature that organization A sees with a geometry: its own and the shared one.</summary>
    public IReadOnlyList<string> VisibleToA =>
        [OverrideId, RuleId, RuledNoMatchId, DefaultLayerId, PlainId, SharedId];

    public BoundingBox Whole => new(Lon - 0.1, Lat - 0.1, Lon + 0.1, Lat + 0.1);

    /// <summary>
    /// What organization A sees with a geometry at <paramref name="zoom"/> (XD01-430): the plain type's features at every
    /// zoom, the default-layer one up to <see cref="DefaultLayerMaxZoom"/>, the ruled ones from <see cref="RuledMinZoom"/>.
    /// </summary>
    public IReadOnlyList<string> VisibleToAAt(int zoom) =>
    [
        .. zoom >= RuledMinZoom ? new[] { OverrideId, RuleId, RuledNoMatchId } : [],
        .. zoom <= DefaultLayerMaxZoom ? new[] { DefaultLayerId } : [],
        PlainId,
        SharedId,
    ];

    /// <summary>A box holding only the plain feature (see <see cref="PlainId"/>).</summary>
    public BoundingBox AroundPlain => BoundingBox.Around(Lon + PlainOffset, Lat + PlainOffset, 0.0005, 0.0005);

    /// <summary>A box over the area's far corner, holding none of the features.</summary>
    public BoundingBox Empty => new(Lon + 0.08, Lat + 0.08, Lon + 0.09, Lat + 0.09);

    public PostgresOrganizationAssetCatalogs Catalogs => _catalogs!;

    public static async Task<RenderQueryWorld> SeedAsync(PostgresContainerFixture fixture)
    {
        var world = new RenderQueryWorld(fixture);
        await world.SeedRowsAsync();
        await world.OpenCatalogsAsync();
        return world;
    }

    private string NewId(string prefix)
    {
        var id = $"{prefix}-{Guid.NewGuid():N}"[..30];
        _featureIds.Add(id);
        return id;
    }

    private GeoFeature Feature(
        string id, AssetType type, Guid organizationId, double offset, string? layerId = null,
        params (string Key, string Value)[] attributes) => new()
    {
        Id = id,
        Geometry = new GeoPoint(Lon + offset, Lat + offset),
        Properties = new GeoFeatureProperties
        {
            Name = $"name {id}",
            Description = $"description {id}",
            AssetTypeId = type.Id.ToString(),
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

        var overrideLayer = NewLayer(provider, OverrideColor);
        var ruleLayer = NewLayer(provider, RuleColor);
        var defaultLayer = NewLayer(provider, DefaultLayerColor);

        DefaultLayerType = NewType(provider, "default-layer", "#aaaaaa", defaultLayer.Id, maxZoom: DefaultLayerMaxZoom);
        RuledType = NewType(provider, "ruled", RuledTypeColor, null, minZoom: RuledMinZoom);
        PlainType = NewType(provider, "plain", PlainTypeColor, null);

        var rule = new LayerRule
        {
            AssetTypeId = RuledType.Id,
            LayerId = ruleLayer.Id,
            Priority = 1,
            Conditions = [new LayerRuleCondition { Attribute = "voltage", Operator = LayerRuleOperator.Equals, Value = "high" }],
        };
        _layerRuleIds.Add(rule.Id);
        provider.AddLayerRule(rule);

        OverrideId = NewId("override");
        RuleId = NewId("rule");
        RuledNoMatchId = NewId("ruled-low");
        DefaultLayerId = NewId("default");
        PlainId = NewId("plain");
        SharedId = NewId("shared");
        OrgBId = NewId("org-b");
        GeometrylessId = NewId("nogeom");

        var geometryless = Feature(GeometrylessId, PlainType, OrgA, 0.07);
        geometryless.Geometry = null;

        provider.AddRange(
        [
            Feature(OverrideId, RuledType, OrgA, 0.0010, overrideLayer.Id.ToString(), ("voltage", "high")),
            Feature(RuleId, RuledType, OrgA, 0.0020, null, ("voltage", "high")),
            Feature(RuledNoMatchId, RuledType, OrgA, 0.0030, null, ("voltage", "low")),
            Feature(DefaultLayerId, DefaultLayerType, OrgA, 0.0040),
            Feature(PlainId, PlainType, OrgA, PlainOffset),
            Feature(SharedId, PlainType, Guid.Empty, 0.0060),
            Feature(OrgBId, PlainType, OrgB, 0.0070),
            geometryless,
        ]);
    }

    private Layer NewLayer(PostgresAssetProvider provider, string color)
    {
        var layer = new Layer { Name = $"render-{color}", GeometryType = GeometryType.Point, Color = color };
        _layerIds.Add(layer.Id);
        provider.AddLayer(layer);
        return layer;
    }

    private AssetType NewType(
        PostgresAssetProvider provider, string name, string color, Guid? defaultLayerId, int? minZoom = null, int? maxZoom = null)
    {
        var type = new AssetType
        {
            Name = $"render-{name}-{Guid.NewGuid():N}", Color = color, DefaultLayerId = defaultLayerId, MinZoom = minZoom, MaxZoom = maxZoom,
        };
        _assetTypeIds.Add(type.Id);
        provider.AddAssetType(type);
        return type;
    }

    /// <summary>The host's arrangement, after seeding: the warmed tenant-less provider as the shared catalog.</summary>
    private async Task OpenCatalogsAsync()
    {
        var factory = new PostgresProviderFactory(NullLoggerFactory.Instance, TimeProvider.System);
        _shared = new ValidatingAssetProvider(await factory.CreateAsync(_fixture.ConnectionString));
        _catalogs = new PostgresOrganizationAssetCatalogs(
            _fixture.CreateOptions(), _shared, NullLoggerFactory.Instance, TimeProvider.System);
        await _catalogs.WarmAsync(OrgA, CancellationToken.None);
        await _catalogs.WarmAsync(OrgB, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        if (_catalogs is not null)
            await _catalogs.DisposeAsync();
        if (_shared is not null)
            await _shared.DisposeAsync();

        await using var db = new GeoAssetsDbContext(_fixture.CreateOptions());
        await db.GeoEntities.IgnoreQueryFilters().Where(e => _featureIds.Contains(e.Id)).ExecuteDeleteAsync();
        await db.LayerRules.Where(r => _layerRuleIds.Contains(r.Id)).ExecuteDeleteAsync();
        await db.AssetTypes.Where(t => _assetTypeIds.Contains(t.Id)).ExecuteDeleteAsync();
        await db.Layers.Where(l => _layerIds.Contains(l.Id)).ExecuteDeleteAsync();
    }
}
