using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Diagnostics;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Shared.Services.Observability;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Observability;

public class ObservableAssetProviderTests
{
    private static GeoFeature Feature(string id, string assetTypeId) => new()
    {
        Id = id,
        Properties = { AssetTypeId = assetTypeId },
    };

    private static ObservableAssetProvider Sut(TestAssetProvider inner) =>
        new(inner, NullLogger<ObservableAssetProvider>.Instance, TimeProvider.System);

    private static Activity? CaptureSpan(Action act)
    {
        Activity? captured = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ImportDiagnostics.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => captured = activity
        };
        ActivitySource.AddActivityListener(listener);

        act();

        return captured;
    }

    // ── Instrumented ─────────────────────────────────────────────────────────

    [Fact]
    public void GetAll_EmitsSpanWithFeatureCountTag()
    {
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var sut = Sut(inner);
        IReadOnlyList<GeoFeature>? result = null;

        var captured = CaptureSpan(() => result = sut.GetAll());

        result.Should().ContainSingle();
        captured.Should().NotBeNull();
        captured!.OperationName.Should().Be("repository.get_all");
        captured.GetTagItem("feature.count").Should().Be(1);
        captured.GetTagItem("duration_ms").Should().NotBeNull();
    }

    [Fact]
    public async Task GetInBoundsAsync_EmitsSpanWithFeatureCountTag()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Geometry = new GeoPoint(1, 1) });
        var sut = Sut(inner);
        IReadOnlyList<GeoFeature>? result = null;

        var captured = CaptureSpan(() => result = sut.GetInBoundsAsync(0, 0, 2, 2).GetAwaiter().GetResult());

        result.Should().ContainSingle();
        captured.Should().NotBeNull();
        captured!.OperationName.Should().Be("repository.get_in_bounds");
        captured.GetTagItem("feature.count").Should().Be(1);
    }

    [Fact]
    public async Task GetInBoundsJsonAsync_EmitsSpanWithFeatureCountTag()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Geometry = new GeoPoint(1, 1) });
        var sut = Sut(inner);
        IReadOnlyList<JsonElement>? result = null;

        var captured = CaptureSpan(() => result = sut.GetInBoundsJsonAsync(0, 0, 2, 2).GetAwaiter().GetResult());

        result.Should().ContainSingle();
        result![0].ValueKind.Should().Be(JsonValueKind.Object);
        captured.Should().NotBeNull();
        captured!.OperationName.Should().Be("repository.get_in_bounds_json");
        captured.GetTagItem("feature.count").Should().Be(1);
    }

    // ── Pass-through: reads ──────────────────────────────────────────────────

    [Fact]
    public void GetById_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var sut = Sut(inner);

        sut.GetById("a")!.Id.Should().Be("a");
    }

    [Fact]
    public void GetByAssetType_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var sut = Sut(inner);

        sut.GetByAssetType(AssetType.Point.Id.ToString()).Should().ContainSingle();
    }

    [Fact]
    public void Search_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var feature = Feature("a", AssetType.Point.Id.ToString());
        feature.Properties.Name = "Downtown Hydrant";
        inner.Add(feature);
        var sut = Sut(inner);

        sut.Search("downtown").Should().ContainSingle();
    }

    [Fact]
    public async Task GetPageAsync_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var sut = Sut(inner);

        var result = await sut.GetPageAsync(new AssetQuery());

        result.Items.Should().ContainSingle();
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public void GetWithin_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Geometry = new GeoPoint(1, 1) });
        var sut = Sut(inner);
        var bounds = new GeoPolygon([(0d, 0d), (2d, 0d), (2d, 2d), (0d, 2d), (0d, 0d)]);

        sut.GetWithin(bounds).Should().ContainSingle();
    }

    [Fact]
    public void GetIntersecting_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Geometry = new GeoPoint(1, 1) });
        var sut = Sut(inner);
        var bounds = new GeoPolygon([(0d, 0d), (2d, 0d), (2d, 2d), (0d, 2d), (0d, 0d)]);

        sut.GetIntersecting(bounds).Should().ContainSingle();
    }

    [Fact]
    public async Task GetInBoundsRawJsonAsync_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var sut = Sut(inner);

        // TestAssetProvider doesn't override the raw-JSON default (returns null) — this still
        // proves the decorator forwards the call directly, unlike the instrumented members above.
        (await sut.GetInBoundsRawJsonAsync(0, 0, 2, 2)).Should().BeNull();
    }

    [Fact]
    public void GetNearby_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Geometry = new GeoPoint(0, 0) });
        var sut = Sut(inner);

        sut.GetNearby(new GeoPoint(0, 0), 1).Should().ContainSingle();
    }

    [Fact]
    public void GetNeighbors_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });
        var sut = Sut(inner);

        sut.GetNeighbors("a").Should().ContainSingle(f => f.Id == "b");
    }

    [Fact]
    public void GetDescendants_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });
        var sut = Sut(inner);

        sut.GetDescendants("a").Should().ContainSingle(f => f.Id == "b");
    }

    [Fact]
    public void GetAncestors_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });
        var sut = Sut(inner);

        sut.GetAncestors("b").Should().ContainSingle(f => f.Id == "a");
    }

    [Fact]
    public void FindPath_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });
        var sut = Sut(inner);

        sut.FindPath("a", "b").Should().HaveCount(2);
    }

    [Fact]
    public void FindShortestPath_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b", Weight = 1 }] });
        inner.Add(new GeoFeature { Id = "b" });
        var sut = Sut(inner);

        sut.FindShortestPath("a", "b").Should().HaveCount(2);
    }

    [Fact]
    public void GetConnectedComponents_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });
        var sut = Sut(inner);

        sut.GetConnectedComponents().Should().ContainSingle();
    }

    [Fact]
    public void HasCycles_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "a" }] });
        var sut = Sut(inner);

        sut.HasCycles().Should().BeTrue();
    }

    [Fact]
    public void TopologicalSort_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });
        var sut = Sut(inner);

        sut.TopologicalSort().Should().HaveCount(2);
    }

    [Fact]
    public void GetAssetTypes_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var sut = Sut(inner);

        sut.GetAssetTypes().Should().BeEquivalentTo(AssetType.Defaults);
    }

    [Fact]
    public void GetLayers_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.AddLayer(new Layer { Name = "Custom" });
        var sut = Sut(inner);

        sut.GetLayers().Should().ContainSingle();
    }

    [Fact]
    public void GetLayerRules_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var assetTypeId = Guid.NewGuid();
        inner.AddLayerRule(new LayerRule { AssetTypeId = assetTypeId });
        var sut = Sut(inner);

        sut.GetLayerRules(assetTypeId).Should().ContainSingle();
    }

    // ── Pass-through: writes ─────────────────────────────────────────────────

    [Fact]
    public void Add_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var sut = Sut(inner);

        sut.Add(Feature("a", AssetType.Point.Id.ToString()));

        inner.GetById("a").Should().NotBeNull();
    }

    [Fact]
    public void Update_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var sut = Sut(inner);
        var updated = Feature("a", AssetType.Point.Id.ToString());
        updated.Properties.Name = "Updated";

        sut.Update(updated);

        inner.GetById("a")!.Properties.Name.Should().Be("Updated");
    }

    [Fact]
    public void AddRange_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var sut = Sut(inner);

        sut.AddRange([Feature("a", AssetType.Point.Id.ToString())]);

        inner.GetById("a").Should().NotBeNull();
    }

    [Fact]
    public void Delete_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var sut = Sut(inner);

        sut.Delete("a");

        inner.GetById("a").Should().BeNull();
    }

    [Fact]
    public void Clear_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var sut = Sut(inner);

        sut.Clear();

        inner.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void LoadAll_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var sut = Sut(inner);

        sut.LoadAll([Feature("a", AssetType.Point.Id.ToString())]);

        inner.GetAll().Should().ContainSingle();
    }

    [Fact]
    public void AddAssetType_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var sut = Sut(inner);
        var type = new AssetType { Name = "Custom" };

        sut.AddAssetType(type);

        inner.GetAssetTypes().Should().Contain(t => t.Id == type.Id);
    }

    [Fact]
    public void DeleteAssetType_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var type = new AssetType { Name = "Custom" };
        inner.AddAssetType(type);
        var sut = Sut(inner);

        sut.DeleteAssetType(type.Id);

        inner.GetAssetTypes().Should().NotContain(t => t.Id == type.Id);
    }

    [Fact]
    public void AddLayer_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var sut = Sut(inner);
        var layer = new Layer { Name = "Custom" };

        sut.AddLayer(layer);

        inner.GetLayers().Should().Contain(l => l.Id == layer.Id);
    }

    [Fact]
    public void DeleteLayer_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var layer = new Layer { Name = "Custom" };
        inner.AddLayer(layer);
        var sut = Sut(inner);

        sut.DeleteLayer(layer.Id);

        inner.GetLayers().Should().NotContain(l => l.Id == layer.Id);
    }

    [Fact]
    public void AddLayerRule_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var sut = Sut(inner);
        var rule = new LayerRule();

        sut.AddLayerRule(rule);

        inner.GetLayerRules(rule.AssetTypeId).Should().Contain(r => r.Id == rule.Id);
    }

    [Fact]
    public void DeleteLayerRule_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var rule = new LayerRule();
        inner.AddLayerRule(rule);
        var sut = Sut(inner);

        sut.DeleteLayerRule(rule.Id);

        inner.GetLayerRules(rule.AssetTypeId).Should().NotContain(r => r.Id == rule.Id);
    }

    // ── Event forwarding ─────────────────────────────────────────────────────

    [Fact]
    public void FeatureAdded_SubscribeThenUnsubscribe_ForwardsThenStops()
    {
        var inner = new TestAssetProvider();
        var sut = Sut(inner);
        var count = 0;
        void Handler(object? _, GeoFeature f) => count++;

        sut.FeatureAdded += Handler;
        sut.Add(Feature("a", AssetType.Point.Id.ToString()));
        sut.FeatureAdded -= Handler;
        sut.Add(Feature("b", AssetType.Point.Id.ToString()));

        count.Should().Be(1);
    }

    [Fact]
    public void FeatureUpdated_SubscribeThenUnsubscribe_ForwardsThenStops()
    {
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var sut = Sut(inner);
        var count = 0;
        void Handler(object? _, GeoFeature f) => count++;

        sut.FeatureUpdated += Handler;
        sut.Update(Feature("a", AssetType.Point.Id.ToString()));
        sut.FeatureUpdated -= Handler;
        sut.Update(Feature("a", AssetType.Point.Id.ToString()));

        count.Should().Be(1);
    }

    [Fact]
    public void FeatureDeleted_SubscribeThenUnsubscribe_ForwardsThenStops()
    {
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        inner.Add(Feature("b", AssetType.Point.Id.ToString()));
        var sut = Sut(inner);
        var count = 0;
        void Handler(object? _, string id) => count++;

        sut.FeatureDeleted += Handler;
        sut.Delete("a");
        sut.FeatureDeleted -= Handler;
        sut.Delete("b");

        count.Should().Be(1);
    }

    [Fact]
    public void CollectionChanged_SubscribeThenUnsubscribe_ForwardsThenStops()
    {
        var inner = new TestAssetProvider();
        var sut = Sut(inner);
        var count = 0;
        void Handler(object? _, EventArgs e) => count++;

        sut.CollectionChanged += Handler;
        sut.Add(Feature("a", AssetType.Point.Id.ToString()));
        sut.CollectionChanged -= Handler;
        sut.Add(Feature("b", AssetType.Point.Id.ToString()));

        count.Should().Be(1);
    }
}
