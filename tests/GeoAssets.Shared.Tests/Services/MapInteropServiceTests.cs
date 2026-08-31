using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Provider.InMemory;
using GeoAssets.Shared.Services;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

public class MapInteropServiceTests
{
    private static GeoFeature Feature(string id, Guid assetTypeId, string? layerId = null, params (string key, string value)[] attributes)
    {
        var feature = new GeoFeature { Id = id, Properties = { AssetTypeId = assetTypeId.ToString() } };
        if (layerId is not null) feature.Properties.LayerId = layerId;
        foreach (var (key, value) in attributes)
            feature.Properties.CustomAttributes[key] = value;
        return feature;
    }

    // ── No resolution possible ────────────────────────────────────────────────

    [Fact]
    public void BuildStyleMap_UnknownAssetType_OmitsFeature()
    {
        var repo = new InMemoryAssetProvider();
        var feature = Feature("a", Guid.NewGuid());

        var map = MapInteropService.BuildStyleMap(repo, [feature]);

        map.Should().BeEmpty();
    }

    [Fact]
    public void BuildStyleMap_KnownAssetTypeButNothingResolves_OmitsFeature()
    {
        var repo = new InMemoryAssetProvider();
        var assetType = new AssetType { Name = "Plain" }; // no DefaultLayerId, no rules
        repo.AddAssetType(assetType);
        var feature = Feature("a", assetType.Id);

        var map = MapInteropService.BuildStyleMap(repo, [feature]);

        map.Should().BeEmpty();
    }

    // ── Resolution present ────────────────────────────────────────────────────

    [Fact]
    public void BuildStyleMap_DefaultLayerResolves_MapsFeatureToLayerStyle()
    {
        var repo = new InMemoryAssetProvider();
        var layer = new Layer { Name = "Default", GeometryType = GeometryType.Point, Color = "#ff0000", Radius = 6 };
        repo.AddLayer(layer);
        var assetType = new AssetType { Name = "Pole", DefaultLayerId = layer.Id };
        repo.AddAssetType(assetType);
        var feature = Feature("a", assetType.Id);

        var map = MapInteropService.BuildStyleMap(repo, [feature]);

        map.Should().ContainKey("a");
        map["a"].Color.Should().Be("#ff0000");
        map["a"].Radius.Should().Be(6);
    }

    [Fact]
    public void BuildStyleMap_TwoFeaturesSameTypeDifferentAttributes_MatchDifferentRules()
    {
        // Reproduces the ticket's acceptance criterion directly: two features of the same
        // AssetType but with CustomAttributes matching different LayerRules resolve to
        // visibly different styles.
        var repo = new InMemoryAssetProvider();
        var steelLayer = new Layer { Name = "Steel", Color = "#111111", Weight = 5 };
        var woodLayer = new Layer { Name = "Wood", Color = "#8b5a2b", Weight = 2 };
        repo.AddLayer(steelLayer);
        repo.AddLayer(woodLayer);

        var assetType = new AssetType { Name = "Pole" };
        repo.AddAssetType(assetType);

        repo.AddLayerRule(new LayerRule
        {
            AssetTypeId = assetType.Id,
            LayerId = steelLayer.Id,
            Conditions = [new LayerRuleCondition { Attribute = "material", Operator = LayerRuleOperator.Equals, Value = "steel" }]
        });
        repo.AddLayerRule(new LayerRule
        {
            AssetTypeId = assetType.Id,
            LayerId = woodLayer.Id,
            Conditions = [new LayerRuleCondition { Attribute = "material", Operator = LayerRuleOperator.Equals, Value = "wood" }]
        });

        var steelFeature = Feature("steel-pole", assetType.Id, attributes: ("material", "steel"));
        var woodFeature = Feature("wood-pole", assetType.Id, attributes: ("material", "wood"));

        var map = MapInteropService.BuildStyleMap(repo, [steelFeature, woodFeature]);

        map["steel-pole"].Color.Should().Be("#111111");
        map["steel-pole"].Weight.Should().Be(5);
        map["wood-pole"].Color.Should().Be("#8b5a2b");
        map["wood-pole"].Weight.Should().Be(2);
        map["steel-pole"].Should().NotBe(map["wood-pole"]);
    }

    [Fact]
    public void BuildStyleMap_PerFeatureLayerIdOverride_TakesPriorityOverDefault()
    {
        var repo = new InMemoryAssetProvider();
        var defaultLayer = new Layer { Name = "Default", Color = "#3388ff" };
        var overrideLayer = new Layer { Name = "Override", Color = "#00ff00" };
        repo.AddLayer(defaultLayer);
        repo.AddLayer(overrideLayer);
        var assetType = new AssetType { Name = "Pole", DefaultLayerId = defaultLayer.Id };
        repo.AddAssetType(assetType);
        var feature = Feature("a", assetType.Id, layerId: overrideLayer.Id.ToString());

        var map = MapInteropService.BuildStyleMap(repo, [feature]);

        map["a"].Color.Should().Be("#00ff00");
    }

    // ── Empty/null fields ──────────────────────────────────────────────────────

    [Fact]
    public void BuildStyleMap_LayerWithNoDashArrayOrIcon_MapsToNull()
    {
        var repo = new InMemoryAssetProvider();
        var layer = new Layer { Name = "Plain" }; // DashArray/IconUrl left at their empty defaults
        repo.AddLayer(layer);
        var assetType = new AssetType { Name = "Pole", DefaultLayerId = layer.Id };
        repo.AddAssetType(assetType);
        var feature = Feature("a", assetType.Id);

        var map = MapInteropService.BuildStyleMap(repo, [feature]);

        map["a"].DashArray.Should().BeNull();
        map["a"].IconUrl.Should().BeNull();
    }

    [Fact]
    public void BuildStyleMap_LayerWithDashArrayAndIcon_MapsThemThrough()
    {
        var repo = new InMemoryAssetProvider();
        var layer = new Layer { Name = "Line", DashArray = "5, 5", IconUrl = "/icons/pole.png" };
        repo.AddLayer(layer);
        var assetType = new AssetType { Name = "Wire", DefaultLayerId = layer.Id };
        repo.AddAssetType(assetType);
        var feature = Feature("a", assetType.Id);

        var map = MapInteropService.BuildStyleMap(repo, [feature]);

        map["a"].DashArray.Should().Be("5, 5");
        map["a"].IconUrl.Should().Be("/icons/pole.png");
    }

    // ── No regression fallback ────────────────────────────────────────────────

    [Fact]
    public void BuildStyleMap_MultipleFeatures_OnlyResolvedOnesAppearInMap()
    {
        var repo = new InMemoryAssetProvider();
        var layer = new Layer { Name = "Default", Color = "#123456" };
        repo.AddLayer(layer);
        var withDefault = new AssetType { Name = "HasDefault", DefaultLayerId = layer.Id };
        var withoutDefault = new AssetType { Name = "NoDefault" };
        repo.AddAssetType(withDefault);
        repo.AddAssetType(withoutDefault);

        var resolved = Feature("resolved", withDefault.Id);
        var unresolved = Feature("unresolved", withoutDefault.Id);

        var map = MapInteropService.BuildStyleMap(repo, [resolved, unresolved]);

        map.Should().ContainKey("resolved");
        map.Should().NotContainKey("unresolved"); // geoassets.js falls back to colorMap/default for this one
    }
}
