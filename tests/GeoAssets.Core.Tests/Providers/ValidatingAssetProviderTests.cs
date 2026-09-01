using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Services;
using GeoAssets.Core.Tests;
using Xunit;

namespace GeoAssets.Core.Tests.Providers;

public class ValidatingAssetProviderTests
{
    private const string HydrantSchema = """
    {
      "type": "object",
      "properties": { "diameter_mm": { "type": "integer" } },
      "required": ["diameter_mm"]
    }
    """;

    private static AssetType SchemaAssetType() => new()
    {
        Name = "Hydrant",
        AttributesSchemaJson = HydrantSchema,
    };

    private static GeoFeature Feature(string id, string assetTypeId) => new()
    {
        Id = id,
        Properties = { AssetTypeId = assetTypeId },
    };

    // ── Read pass-through ──────────────────────────────────────────────────────

    [Fact]
    public void GetById_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var sut = new ValidatingAssetProvider(inner);

        sut.GetById("a")!.Id.Should().Be("a");
    }

    [Fact]
    public void GetAll_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var sut = new ValidatingAssetProvider(inner);

        sut.GetAll().Should().ContainSingle();
    }

    [Fact]
    public void GetByAssetType_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var sut = new ValidatingAssetProvider(inner);

        sut.GetByAssetType(AssetType.Point.Id.ToString()).Should().ContainSingle();
    }

    [Fact]
    public void Search_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var feature = Feature("a", AssetType.Point.Id.ToString());
        feature.Properties.Name = "Downtown Hydrant";
        inner.Add(feature);
        var sut = new ValidatingAssetProvider(inner);

        sut.Search("downtown").Should().ContainSingle();
    }

    [Fact]
    public async Task GetPageAsync_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var sut = new ValidatingAssetProvider(inner);

        var result = await sut.GetPageAsync(new AssetQuery());

        result.Items.Should().ContainSingle();
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public void GetWithin_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var feature = new GeoFeature { Id = "a", Geometry = new GeoPoint(1, 1) };
        inner.Add(feature);
        var sut = new ValidatingAssetProvider(inner);
        var bounds = new GeoPolygon([(0d, 0d), (2d, 0d), (2d, 2d), (0d, 2d), (0d, 0d)]);

        sut.GetWithin(bounds).Should().ContainSingle();
    }

    [Fact]
    public void GetIntersecting_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var feature = new GeoFeature { Id = "a", Geometry = new GeoPoint(1, 1) };
        inner.Add(feature);
        var sut = new ValidatingAssetProvider(inner);
        var bounds = new GeoPolygon([(0d, 0d), (2d, 0d), (2d, 2d), (0d, 2d), (0d, 0d)]);

        sut.GetIntersecting(bounds).Should().ContainSingle();
    }

    [Fact]
    public async Task GetInBoundsAsync_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Geometry = new GeoPoint(1, 1) });
        var sut = new ValidatingAssetProvider(inner);

        (await sut.GetInBoundsAsync(0, 0, 2, 2)).Should().ContainSingle();
    }

    [Fact]
    public async Task GetInBoundsJsonAsync_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Geometry = new GeoPoint(1, 1) });
        var sut = new ValidatingAssetProvider(inner);

        var result = await sut.GetInBoundsJsonAsync(0, 0, 2, 2);

        result.Should().ContainSingle();
        result[0].ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task GetInBoundsRawJsonAsync_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var sut = new ValidatingAssetProvider(inner);

        // TestAssetProvider doesn't override the raw-JSON default (returns null) —
        // this still proves the decorator forwards the call instead of short-circuiting.
        (await sut.GetInBoundsRawJsonAsync(0, 0, 2, 2)).Should().BeNull();
    }

    [Fact]
    public void GetNearby_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Geometry = new GeoPoint(0, 0) });
        var sut = new ValidatingAssetProvider(inner);

        sut.GetNearby(new GeoPoint(0, 0), 1).Should().ContainSingle();
    }

    [Fact]
    public void GetNeighbors_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });
        var sut = new ValidatingAssetProvider(inner);

        sut.GetNeighbors("a").Should().ContainSingle(f => f.Id == "b");
    }

    [Fact]
    public void GetDescendants_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });
        var sut = new ValidatingAssetProvider(inner);

        sut.GetDescendants("a").Should().ContainSingle(f => f.Id == "b");
    }

    [Fact]
    public void GetAncestors_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });
        var sut = new ValidatingAssetProvider(inner);

        sut.GetAncestors("b").Should().ContainSingle(f => f.Id == "a");
    }

    [Fact]
    public void FindPath_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });
        var sut = new ValidatingAssetProvider(inner);

        sut.FindPath("a", "b").Should().HaveCount(2);
    }

    [Fact]
    public void FindShortestPath_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b", Weight = 1 }] });
        inner.Add(new GeoFeature { Id = "b" });
        var sut = new ValidatingAssetProvider(inner);

        sut.FindShortestPath("a", "b").Should().HaveCount(2);
    }

    [Fact]
    public void GetConnectedComponents_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });
        var sut = new ValidatingAssetProvider(inner);

        sut.GetConnectedComponents().Should().ContainSingle();
    }

    [Fact]
    public void HasCycles_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "a" }] });
        var sut = new ValidatingAssetProvider(inner);

        sut.HasCycles().Should().BeTrue();
    }

    [Fact]
    public void TopologicalSort_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });
        var sut = new ValidatingAssetProvider(inner);

        sut.TopologicalSort().Should().HaveCount(2);
    }

    [Fact]
    public void GetAssetTypes_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var sut = new ValidatingAssetProvider(inner);

        sut.GetAssetTypes().Should().BeEquivalentTo(AssetType.Defaults);
    }

    // ── Add / Update attribute-schema validation (XD01-10) ──────────────────────

    [Fact]
    public void Add_UnknownAssetType_SkipsValidation()
    {
        var inner = new TestAssetProvider();
        var sut = new ValidatingAssetProvider(inner);

        sut.Add(Feature("a", Guid.NewGuid().ToString()));

        inner.GetById("a").Should().NotBeNull();
    }

    [Fact]
    public void Add_AssetTypeWithNoSchema_SkipsValidation()
    {
        var inner = new TestAssetProvider();
        var sut = new ValidatingAssetProvider(inner);

        sut.Add(Feature("a", AssetType.Point.Id.ToString()));

        inner.GetById("a").Should().NotBeNull();
    }

    [Fact]
    public void Add_ValidAttributes_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var assetType = SchemaAssetType();
        inner.AddAssetType(assetType);
        var sut = new ValidatingAssetProvider(inner);

        var feature = Feature("a", assetType.Id.ToString());
        feature.Properties.CustomAttributes["diameter_mm"] = "100";

        sut.Add(feature);

        inner.GetById("a").Should().NotBeNull();
    }

    [Fact]
    public void Add_InvalidAttributes_ThrowsAndDoesNotCallInner()
    {
        var inner = new TestAssetProvider();
        var assetType = SchemaAssetType();
        inner.AddAssetType(assetType);
        var sut = new ValidatingAssetProvider(inner);

        var feature = Feature("a", assetType.Id.ToString()); // missing required diameter_mm

        var act = () => sut.Add(feature);

        act.Should().Throw<GeoFeatureAttributeValidationException>()
            .Which.AssetTypeId.Should().Be(assetType.Id);
        inner.GetById("a").Should().BeNull();
    }

    [Fact]
    public void Update_ValidAttributes_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var assetType = SchemaAssetType();
        inner.AddAssetType(assetType);
        inner.Add(Feature("a", assetType.Id.ToString()));
        var sut = new ValidatingAssetProvider(inner);

        var updated = Feature("a", assetType.Id.ToString());
        updated.Properties.CustomAttributes["diameter_mm"] = "50";

        sut.Update(updated);

        inner.GetById("a")!.Properties.CustomAttributes["diameter_mm"].Should().Be("50");
    }

    [Fact]
    public void Update_InvalidAttributes_ThrowsAndDoesNotCallInner()
    {
        var inner = new TestAssetProvider();
        var assetType = SchemaAssetType();
        inner.AddAssetType(assetType);
        var original = Feature("a", assetType.Id.ToString());
        original.Properties.CustomAttributes["diameter_mm"] = "100";
        inner.Add(original);
        var sut = new ValidatingAssetProvider(inner);

        var updated = Feature("a", assetType.Id.ToString()); // missing required diameter_mm

        var act = () => sut.Update(updated);

        act.Should().Throw<GeoFeatureAttributeValidationException>();
        inner.GetById("a")!.Properties.CustomAttributes.Should().ContainKey("diameter_mm");
    }

    // ── Add / Update geometry-shape validation (XD01-111) ────────────────────────

    [Fact]
    public void Add_UnknownAssetType_SkipsGeometryValidation()
    {
        var inner = new TestAssetProvider();
        var sut = new ValidatingAssetProvider(inner);
        var feature = Feature("a", Guid.NewGuid().ToString());
        feature.Geometry = new GeoPoint(1, 1);

        sut.Add(feature);

        inner.GetById("a").Should().NotBeNull();
    }

    [Fact]
    public void Add_UnrestrictedAssetType_AcceptsAnyGeometry()
    {
        var inner = new TestAssetProvider();
        var assetType = new AssetType { Name = "Any shape" }; // AllowedGeometryType null
        inner.AddAssetType(assetType);
        var sut = new ValidatingAssetProvider(inner);
        var feature = Feature("a", assetType.Id.ToString());
        feature.Geometry = new GeoLineString([(0d, 0d), (1d, 1d)]);

        sut.Add(feature);

        inner.GetById("a").Should().NotBeNull();
    }

    [Fact]
    public void Add_NoGeometryYet_SkipsGeometryValidation()
    {
        var inner = new TestAssetProvider();
        var assetType = new AssetType { Name = "Hydrant", AllowedGeometryType = GeometryType.Point };
        inner.AddAssetType(assetType);
        var sut = new ValidatingAssetProvider(inner);
        var feature = Feature("a", assetType.Id.ToString()); // Geometry is null

        sut.Add(feature);

        inner.GetById("a").Should().NotBeNull();
    }

    [Fact]
    public void Add_MatchingGeometry_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var assetType = new AssetType { Name = "Hydrant", AllowedGeometryType = GeometryType.Point };
        inner.AddAssetType(assetType);
        var sut = new ValidatingAssetProvider(inner);
        var feature = Feature("a", assetType.Id.ToString());
        feature.Geometry = new GeoPoint(1, 1);

        sut.Add(feature);

        inner.GetById("a").Should().NotBeNull();
    }

    [Fact]
    public void Add_MismatchedGeometry_ThrowsAndDoesNotCallInner()
    {
        var inner = new TestAssetProvider();
        var assetType = new AssetType { Name = "Hydrant", AllowedGeometryType = GeometryType.Point };
        inner.AddAssetType(assetType);
        var sut = new ValidatingAssetProvider(inner);
        var feature = Feature("a", assetType.Id.ToString());
        feature.Geometry = new GeoLineString([(0d, 0d), (1d, 1d)]);

        var act = () => sut.Add(feature);

        act.Should().Throw<GeoFeatureGeometryMismatchException>()
            .Which.Should().Match<GeoFeatureGeometryMismatchException>(e =>
                e.AssetTypeId == assetType.Id &&
                e.Expected == GeometryType.Point &&
                e.Actual == GeometryType.LineString);
        inner.GetById("a").Should().BeNull();
    }

    [Fact]
    public void Update_MismatchedGeometry_ThrowsAndDoesNotCallInner()
    {
        var inner = new TestAssetProvider();
        var assetType = new AssetType { Name = "Hydrant", AllowedGeometryType = GeometryType.Point };
        inner.AddAssetType(assetType);
        var original = Feature("a", assetType.Id.ToString());
        original.Geometry = new GeoPoint(1, 1);
        inner.Add(original);
        var sut = new ValidatingAssetProvider(inner);

        var updated = Feature("a", assetType.Id.ToString());
        updated.Geometry = new GeoPolygon([(0d, 0d), (1d, 0d), (1d, 1d), (0d, 0d)]);

        var act = () => sut.Update(updated);

        act.Should().Throw<GeoFeatureGeometryMismatchException>();
        inner.GetById("a")!.Geometry!.GeometryType.Should().Be(GeometryType.Point);
    }

    // ── Other write pass-through ────────────────────────────────────────────────

    [Fact]
    public void AddRange_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var sut = new ValidatingAssetProvider(inner);

        sut.AddRange([Feature("a", AssetType.Point.Id.ToString())]);

        inner.GetById("a").Should().NotBeNull();
    }

    [Fact]
    public void Delete_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var sut = new ValidatingAssetProvider(inner);

        sut.Delete("a");

        inner.GetById("a").Should().BeNull();
    }

    [Fact]
    public void Clear_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var sut = new ValidatingAssetProvider(inner);

        sut.Clear();

        inner.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void LoadAll_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var sut = new ValidatingAssetProvider(inner);

        sut.LoadAll([Feature("a", AssetType.Point.Id.ToString())]);

        inner.GetAll().Should().ContainSingle();
    }

    [Fact]
    public void AddAssetType_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var sut = new ValidatingAssetProvider(inner);
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
        var sut = new ValidatingAssetProvider(inner);

        sut.DeleteAssetType(type.Id);

        inner.GetAssetTypes().Should().NotContain(t => t.Id == type.Id);
    }

    [Fact]
    public void GetLayers_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        inner.AddLayer(new Layer { Name = "Custom" });
        var sut = new ValidatingAssetProvider(inner);

        sut.GetLayers().Should().ContainSingle();
    }

    [Fact]
    public void AddLayer_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var sut = new ValidatingAssetProvider(inner);
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
        var sut = new ValidatingAssetProvider(inner);

        sut.DeleteLayer(layer.Id);

        inner.GetLayers().Should().NotContain(l => l.Id == layer.Id);
    }

    [Fact]
    public void GetLayerRules_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var assetTypeId = Guid.NewGuid();
        inner.AddLayerRule(new LayerRule { AssetTypeId = assetTypeId });
        var sut = new ValidatingAssetProvider(inner);

        sut.GetLayerRules(assetTypeId).Should().ContainSingle();
    }

    [Fact]
    public void AddLayerRule_DelegatesToInner()
    {
        var inner = new TestAssetProvider();
        var sut = new ValidatingAssetProvider(inner);
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
        var sut = new ValidatingAssetProvider(inner);

        sut.DeleteLayerRule(rule.Id);

        inner.GetLayerRules(rule.AssetTypeId).Should().NotContain(r => r.Id == rule.Id);
    }

    // ── Events (forwarded) ───────────────────────────────────────────────────────

    [Fact]
    public void FeatureAdded_SubscribeThenUnsubscribe_ForwardsThenStops()
    {
        var inner = new TestAssetProvider();
        var sut = new ValidatingAssetProvider(inner);
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
        var sut = new ValidatingAssetProvider(inner);
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
        var sut = new ValidatingAssetProvider(inner);
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
        var sut = new ValidatingAssetProvider(inner);
        var count = 0;
        void Handler(object? _, EventArgs e) => count++;

        sut.CollectionChanged += Handler;
        sut.Add(Feature("a", AssetType.Point.Id.ToString()));
        sut.CollectionChanged -= Handler;
        sut.Add(Feature("b", AssetType.Point.Id.ToString()));

        count.Should().Be(1);
    }

    // ── Disposal forwarding ──────────────────────────────────────────────────────

    /// <summary>Minimal <see cref="IAssetProvider"/> stub — every write/read is a no-op or
    /// empty default, since disposal tests only need a valid decorator target, not behavior.</summary>
    private class StubAssetProvider : IAssetProvider
    {
        public GeoFeature? GetById(string id) => null;
        public IReadOnlyList<GeoFeature> GetAll() => [];
        public IReadOnlyList<GeoFeature> GetByAssetType(string assetTypeId) => [];
        public IReadOnlyList<GeoFeature> Search(string query) => [];
        public IReadOnlyList<GeoFeature> GetWithin(GeoGeometry bounds) => [];
        public IReadOnlyList<GeoFeature> GetIntersecting(GeoGeometry geometry) => [];
        public Task<IReadOnlyList<GeoFeature>> GetInBoundsAsync(double minLon, double minLat, double maxLon, double maxLat) => Task.FromResult<IReadOnlyList<GeoFeature>>([]);
        public Task<IReadOnlyList<JsonElement>> GetInBoundsJsonAsync(double minLon, double minLat, double maxLon, double maxLat) => Task.FromResult<IReadOnlyList<JsonElement>>([]);
        public IReadOnlyList<GeoFeature> GetNearby(GeoPoint center, double distanceDegrees) => [];
        public IReadOnlyList<GeoFeature> GetNeighbors(string featureId) => [];
        public IReadOnlyList<GeoFeature> GetDescendants(string featureId) => [];
        public IReadOnlyList<GeoFeature> GetAncestors(string featureId) => [];
        public IReadOnlyList<GeoFeature> FindPath(string fromId, string toId) => [];
        public IReadOnlyList<GeoFeature> FindShortestPath(string fromId, string toId) => [];
        public IReadOnlyList<IReadOnlyList<GeoFeature>> GetConnectedComponents() => [];
        public bool HasCycles() => false;
        public IReadOnlyList<GeoFeature> TopologicalSort() => [];
        public void Add(GeoFeature feature) { }
        public void Update(GeoFeature feature) { }
        public void AddRange(IEnumerable<GeoFeature> features) { }
        public void Delete(string id) { }
        public void Clear() { }
        public void LoadAll(IEnumerable<GeoFeature> features) { }
        public IReadOnlyList<AssetType> GetAssetTypes() => [];
        public void AddAssetType(AssetType assetType) { }
        public void DeleteAssetType(Guid id) { }
        public IReadOnlyList<Layer> GetLayers() => [];
        public void AddLayer(Layer layer) { }
        public void DeleteLayer(Guid id) { }
        public IReadOnlyList<LayerRule> GetLayerRules(Guid assetTypeId) => [];
        public void AddLayerRule(LayerRule layerRule) { }
        public void DeleteLayerRule(Guid id) { }
        public event EventHandler<GeoFeature>? FeatureAdded { add { } remove { } }
        public event EventHandler<GeoFeature>? FeatureUpdated { add { } remove { } }
        public event EventHandler<string>? FeatureDeleted { add { } remove { } }
        public event EventHandler? CollectionChanged { add { } remove { } }
    }

    private sealed class AsyncDisposableStubProvider : StubAssetProvider, IAsyncDisposable
    {
        public bool Disposed { get; private set; }
        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SyncDisposableStubProvider : StubAssetProvider, IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    [Fact]
    public async Task DisposeAsync_InnerIsAsyncDisposable_DisposesInner()
    {
        var inner = new AsyncDisposableStubProvider();
        var sut = new ValidatingAssetProvider(inner);

        await sut.DisposeAsync();

        inner.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_InnerIsSyncDisposable_DisposesInner()
    {
        var inner = new SyncDisposableStubProvider();
        var sut = new ValidatingAssetProvider(inner);

        await sut.DisposeAsync();

        inner.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task DisposeAsync_InnerNotDisposable_DoesNotThrow()
    {
        var inner = new StubAssetProvider();
        var sut = new ValidatingAssetProvider(inner);

        var act = async () => await sut.DisposeAsync();

        await act.Should().NotThrowAsync();
    }
}
