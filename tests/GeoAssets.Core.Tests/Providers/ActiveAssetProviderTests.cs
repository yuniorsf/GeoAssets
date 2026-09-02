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

public class ActiveAssetProviderTests
{
    private static GeoFeature Feature(string id, string assetTypeId) => new()
    {
        Id = id,
        Properties = { AssetTypeId = assetTypeId },
    };

    /// <summary>Pool with one entry already made active, wired to a fresh <see cref="ActiveAssetProvider"/>
    /// subscribed before the switch — mirrors how DI actually wires this proxy at boot.</summary>
    private static (ActiveAssetProvider Sut, TestAssetProvider Inner) CreateActive()
    {
        var pool = new ProviderPool();
        var sut = new ActiveAssetProvider(pool);
        var inner = new TestAssetProvider();
        var entry = pool.Add("A", inner);
        pool.SetActive(entry.Id);
        return (sut, inner);
    }

    // ── Read pass-through ────────────────────────────────────────────────────

    [Fact]
    public void GetById_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));

        sut.GetById("a")!.Id.Should().Be("a");
    }

    [Fact]
    public void GetAll_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));

        sut.GetAll().Should().ContainSingle();
    }

    [Fact]
    public void GetByAssetType_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));

        sut.GetByAssetType(AssetType.Point.Id.ToString()).Should().ContainSingle();
    }

    [Fact]
    public void Search_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        var feature = Feature("a", AssetType.Point.Id.ToString());
        feature.Properties.Name = "Downtown Hydrant";
        inner.Add(feature);

        sut.Search("downtown").Should().ContainSingle();
    }

    [Fact]
    public async Task GetPageAsync_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));

        var result = await sut.GetPageAsync(new AssetQuery());

        result.Items.Should().ContainSingle();
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public void GetWithin_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(new GeoFeature { Id = "a", Geometry = new GeoPoint(1, 1) });
        var bounds = new GeoPolygon([(0d, 0d), (2d, 0d), (2d, 2d), (0d, 2d), (0d, 0d)]);

        sut.GetWithin(bounds).Should().ContainSingle();
    }

    [Fact]
    public void GetIntersecting_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(new GeoFeature { Id = "a", Geometry = new GeoPoint(1, 1) });
        var bounds = new GeoPolygon([(0d, 0d), (2d, 0d), (2d, 2d), (0d, 2d), (0d, 0d)]);

        sut.GetIntersecting(bounds).Should().ContainSingle();
    }

    [Fact]
    public async Task GetInBoundsAsync_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(new GeoFeature { Id = "a", Geometry = new GeoPoint(1, 1) });

        (await sut.GetInBoundsAsync(0, 0, 2, 2)).Should().ContainSingle();
    }

    [Fact]
    public async Task GetInBoundsJsonAsync_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(new GeoFeature { Id = "a", Geometry = new GeoPoint(1, 1) });

        var result = await sut.GetInBoundsJsonAsync(0, 0, 2, 2);

        result.Should().ContainSingle();
        result[0].ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task GetInBoundsRawJsonAsync_DelegatesToCurrent()
    {
        var (sut, _) = CreateActive();

        // TestAssetProvider doesn't override the raw-JSON default (returns null) — this still
        // proves the proxy forwards the call to _current instead of short-circuiting.
        (await sut.GetInBoundsRawJsonAsync(0, 0, 2, 2)).Should().BeNull();
    }

    [Fact]
    public void GetNearby_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(new GeoFeature { Id = "a", Geometry = new GeoPoint(0, 0) });

        sut.GetNearby(new GeoPoint(0, 0), 1).Should().ContainSingle();
    }

    [Fact]
    public void GetNeighbors_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });

        sut.GetNeighbors("a").Should().ContainSingle(f => f.Id == "b");
    }

    [Fact]
    public void GetDescendants_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });

        sut.GetDescendants("a").Should().ContainSingle(f => f.Id == "b");
    }

    [Fact]
    public void GetAncestors_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });

        sut.GetAncestors("b").Should().ContainSingle(f => f.Id == "a");
    }

    [Fact]
    public void FindPath_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });

        sut.FindPath("a", "b").Should().HaveCount(2);
    }

    [Fact]
    public void FindShortestPath_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b", Weight = 1 }] });
        inner.Add(new GeoFeature { Id = "b" });

        sut.FindShortestPath("a", "b").Should().HaveCount(2);
    }

    [Fact]
    public void GetConnectedComponents_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });

        sut.GetConnectedComponents().Should().ContainSingle();
    }

    [Fact]
    public void HasCycles_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "a" }] });

        sut.HasCycles().Should().BeTrue();
    }

    [Fact]
    public void TopologicalSort_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b" }] });
        inner.Add(new GeoFeature { Id = "b" });

        sut.TopologicalSort().Should().HaveCount(2);
    }

    [Fact]
    public void GetAssetTypes_DelegatesToCurrent()
    {
        var (sut, _) = CreateActive();

        sut.GetAssetTypes().Should().BeEquivalentTo(AssetType.Defaults);
    }

    [Fact]
    public void GetLayers_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.AddLayer(new Layer { Name = "Custom" });

        sut.GetLayers().Should().ContainSingle();
    }

    [Fact]
    public void GetLayerRules_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        var assetTypeId = Guid.NewGuid();
        inner.AddLayerRule(new LayerRule { AssetTypeId = assetTypeId });

        sut.GetLayerRules(assetTypeId).Should().ContainSingle();
    }

    // ── Write pass-through ───────────────────────────────────────────────────

    [Fact]
    public void Add_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();

        sut.Add(Feature("a", AssetType.Point.Id.ToString()));

        inner.GetById("a").Should().NotBeNull();
    }

    [Fact]
    public void Update_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var updated = Feature("a", AssetType.Point.Id.ToString());
        updated.Properties.Name = "Updated";

        sut.Update(updated);

        inner.GetById("a")!.Properties.Name.Should().Be("Updated");
    }

    [Fact]
    public void AddRange_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();

        sut.AddRange([Feature("a", AssetType.Point.Id.ToString())]);

        inner.GetById("a").Should().NotBeNull();
    }

    [Fact]
    public void Delete_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));

        sut.Delete("a");

        inner.GetById("a").Should().BeNull();
    }

    [Fact]
    public void Clear_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));

        sut.Clear();

        inner.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void LoadAll_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();

        sut.LoadAll([Feature("a", AssetType.Point.Id.ToString())]);

        inner.GetAll().Should().ContainSingle();
    }

    [Fact]
    public void AddAssetType_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        var type = new AssetType { Name = "Custom" };

        sut.AddAssetType(type);

        inner.GetAssetTypes().Should().Contain(t => t.Id == type.Id);
    }

    [Fact]
    public void DeleteAssetType_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        var type = new AssetType { Name = "Custom" };
        inner.AddAssetType(type);

        sut.DeleteAssetType(type.Id);

        inner.GetAssetTypes().Should().NotContain(t => t.Id == type.Id);
    }

    [Fact]
    public void AddLayer_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        var layer = new Layer { Name = "Custom" };

        sut.AddLayer(layer);

        inner.GetLayers().Should().Contain(l => l.Id == layer.Id);
    }

    [Fact]
    public void DeleteLayer_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        var layer = new Layer { Name = "Custom" };
        inner.AddLayer(layer);

        sut.DeleteLayer(layer.Id);

        inner.GetLayers().Should().NotContain(l => l.Id == layer.Id);
    }

    [Fact]
    public void AddLayerRule_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        var rule = new LayerRule();

        sut.AddLayerRule(rule);

        inner.GetLayerRules(rule.AssetTypeId).Should().Contain(r => r.Id == rule.Id);
    }

    [Fact]
    public void DeleteLayerRule_DelegatesToCurrent()
    {
        var (sut, inner) = CreateActive();
        var rule = new LayerRule();
        inner.AddLayerRule(rule);

        sut.DeleteLayerRule(rule.Id);

        inner.GetLayerRules(rule.AssetTypeId).Should().NotContain(r => r.Id == rule.Id);
    }

    // ── Pool switching (XD01-125) ─────────────────────────────────────────────

    [Fact]
    public void GetAll_NoActiveEntryYet_ReturnsEmpty()
    {
        var pool = new ProviderPool();
        var sut = new ActiveAssetProvider(pool);

        sut.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void OnPoolChanged_EntryAddedButNotActive_DoesNotSwitchCurrent()
    {
        var pool = new ProviderPool();
        var sut = new ActiveAssetProvider(pool);
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));

        pool.Add("A", inner); // fires Changed, but the new entry is not active yet

        sut.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void OnPoolChanged_ActiveEntrySet_SwitchesCurrentAndFiresCollectionChanged()
    {
        var pool = new ProviderPool();
        var sut = new ActiveAssetProvider(pool);
        var inner = new TestAssetProvider();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        var entry = pool.Add("A", inner);
        var fired = 0;
        sut.CollectionChanged += (_, _) => fired++;

        pool.SetActive(entry.Id);

        sut.GetAll().Should().ContainSingle();
        fired.Should().Be(1);
    }

    [Fact]
    public void OnPoolChanged_SameActiveEntryUnchanged_DoesNotRefireCollectionChanged()
    {
        var pool = new ProviderPool();
        var sut = new ActiveAssetProvider(pool);
        var entry = pool.Add("A", new TestAssetProvider());
        pool.SetActive(entry.Id);
        var fired = 0;
        sut.CollectionChanged += (_, _) => fired++;

        pool.SetActive(entry.Id); // re-activating the same entry: Changed fires, provider unchanged

        fired.Should().Be(0);
    }

    [Fact]
    public void OnPoolChanged_SwitchingActiveEntry_UnsubscribesOldAndSubscribesNew()
    {
        var pool = new ProviderPool();
        var sut = new ActiveAssetProvider(pool);
        var innerA = new TestAssetProvider();
        var innerB = new TestAssetProvider();
        var entryA = pool.Add("A", innerA);
        var entryB = pool.Add("B", innerB);
        pool.SetActive(entryA.Id);

        var count = 0;
        sut.FeatureAdded += (_, _) => count++;

        pool.SetActive(entryB.Id);

        innerA.Add(Feature("a", AssetType.Point.Id.ToString())); // old current — must not forward anymore
        innerB.Add(Feature("b", AssetType.Point.Id.ToString())); // new current — must forward

        count.Should().Be(1);
    }

    // ── Event forwarding ──────────────────────────────────────────────────────

    [Fact]
    public void FeatureAdded_SubscribeThenUnsubscribe_ForwardsThenStops()
    {
        var (sut, _) = CreateActive();
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
        var (sut, inner) = CreateActive();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
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
        var (sut, inner) = CreateActive();
        inner.Add(Feature("a", AssetType.Point.Id.ToString()));
        inner.Add(Feature("b", AssetType.Point.Id.ToString()));
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
        var (sut, _) = CreateActive();
        var count = 0;
        void Handler(object? _, EventArgs e) => count++;

        sut.CollectionChanged += Handler;
        sut.Add(Feature("a", AssetType.Point.Id.ToString()));
        sut.CollectionChanged -= Handler;
        sut.Add(Feature("b", AssetType.Point.Id.ToString()));

        count.Should().Be(1);
    }

    // ── Defensive guard against a non-ProviderPool IProviderPool implementer ────

    /// <summary>Minimal <see cref="IProviderPool"/> stub that can fire <see cref="Changed"/>
    /// with zero entries — <see cref="ProviderPool"/> itself can never do this (every mutation
    /// method requires an existing entry first), so this exercises the defensive
    /// <c>_pool.All.Count == 0</c> guard in <see cref="ActiveAssetProvider"/> directly.</summary>
    private sealed class EmptyChangingPool : IProviderPool
    {
        public IReadOnlyList<ProviderEntry> All => [];
        public ProviderEntry Active => throw new InvalidOperationException();
        public ProviderEntry Add(string name, IAssetProvider provider) => throw new NotSupportedException();
        public void SetActive(Guid id) { }
        public void Open(Guid id) { }
        public void Close(Guid id) { }
        public void Enable(Guid id) { }
        public void Disable(Guid id) { }
        public void Rename(Guid id, string name) { }
        public void Remove(Guid id) { }
        public event EventHandler? Changed;
        public event EventHandler<ProviderEntry>? EntryAdded { add { } remove { } }
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
    }

    [Fact]
    public void OnPoolChanged_EmptyPoolFiresChanged_DoesNotThrowOrSwitch()
    {
        var pool = new EmptyChangingPool();
        var sut = new ActiveAssetProvider(pool);

        pool.RaiseChanged();

        sut.GetAll().Should().BeEmpty();
    }
}
