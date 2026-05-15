using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Provider.InMemory;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

public class InMemoryAssetProviderTests
{
    private static GeoFeature Feature(string id, string? assetTypeId = null) => new()
    {
        Id = id,
        Properties = { AssetTypeId = assetTypeId ?? AssetType.Point.Id.ToString() }
    };

    private static GeoFeature FeatureAt(string id, double lon, double lat) => new()
    {
        Id = id,
        Geometry = new GeoPoint(lon, lat)
    };

    private static GeoPolygon BigBox() => new([
        (-10d, -10d), (10d, -10d), (10d, 10d), (-10d, 10d), (-10d, -10d)
    ]);

    private static GeoFeature TopoFeature(string id, params string[] targets) => new()
    {
        Id = id,
        Topology = [.. targets.Select(t => new TopoEdge { TargetId = t })]
    };

    // ── GetById ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetById_Empty_ReturnsNull()
    {
        new InMemoryAssetProvider().GetById("x").Should().BeNull();
    }

    [Fact]
    public void GetById_Present_ReturnsFeature()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(Feature("a"));
        sut.GetById("a")!.Id.Should().Be("a");
    }

    [Fact]
    public void GetById_MissingId_ReturnsNull()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(Feature("a"));
        sut.GetById("b").Should().BeNull();
    }

    // ── GetAll ─────────────────────────────────────────────────────────────────

    [Fact]
    public void GetAll_Empty_ReturnsEmptyList()
    {
        new InMemoryAssetProvider().GetAll().Should().BeEmpty();
    }

    [Fact]
    public void GetAll_WithFeatures_ReturnsAll()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(Feature("a"));
        sut.Add(Feature("b"));
        sut.GetAll().Select(f => f.Id).Should().BeEquivalentTo(["a", "b"]);
    }

    // ── GetByAssetType ─────────────────────────────────────────────────────────

    [Fact]
    public void GetByAssetType_NoMatch_ReturnsEmpty()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(Feature("a", AssetType.Point.Id.ToString()));
        sut.GetByAssetType(AssetType.Line.Id.ToString()).Should().BeEmpty();
    }

    [Fact]
    public void GetByAssetType_MultipleTypes_ReturnsOnlyMatching()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(Feature("a", AssetType.Point.Id.ToString()));
        sut.Add(Feature("b", AssetType.Line.Id.ToString()));
        sut.Add(Feature("c", AssetType.Point.Id.ToString()));
        sut.GetByAssetType(AssetType.Point.Id.ToString())
            .Select(f => f.Id).Should().BeEquivalentTo(["a", "c"]);
    }

    // ── Search ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Search_EmptyOrWhitespaceQuery_ReturnsAll(string query)
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(Feature("a"));
        sut.Add(Feature("b"));
        sut.Search(query).Should().HaveCount(2);
    }

    [Fact]
    public void Search_MatchesName_ReturnsFeature()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(new GeoFeature { Id = "a", Properties = { Name = "Main Tower" } });
        sut.Add(new GeoFeature { Id = "b", Properties = { Name = "Bridge" } });
        sut.Search("tower").Select(f => f.Id).Should().ContainSingle().Which.Should().Be("a");
    }

    [Fact]
    public void Search_MatchesDescription_ReturnsFeature()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(new GeoFeature { Id = "a", Properties = { Description = "Riverside station" } });
        sut.Add(new GeoFeature { Id = "b", Properties = { Description = "Mountain post" } });
        sut.Search("river").Select(f => f.Id).Should().ContainSingle().Which.Should().Be("a");
    }

    [Fact]
    public void Search_MatchesCustomAttributeKey_ReturnsFeature()
    {
        var sut = new InMemoryAssetProvider();
        var f = new GeoFeature { Id = "a" };
        f.Properties.CustomAttributes["elevation"] = "100";
        sut.Add(f);
        sut.Add(new GeoFeature { Id = "b" });
        sut.Search("elev").Select(x => x.Id).Should().ContainSingle().Which.Should().Be("a");
    }

    [Fact]
    public void Search_MatchesCustomAttributeValue_ReturnsFeature()
    {
        var sut = new InMemoryAssetProvider();
        var f = new GeoFeature { Id = "a" };
        f.Properties.CustomAttributes["tag"] = "HydroPlant";
        sut.Add(f);
        sut.Add(new GeoFeature { Id = "b" });
        sut.Search("hydro").Select(x => x.Id).Should().ContainSingle().Which.Should().Be("a");
    }

    [Fact]
    public void Search_NoMatch_ReturnsEmpty()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(new GeoFeature { Id = "a", Properties = { Name = "Tower" } });
        sut.Search("bridge").Should().BeEmpty();
    }

    // ── Add ────────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_StoresFeature()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(Feature("a"));
        sut.GetById("a").Should().NotBeNull();
    }

    [Fact]
    public void Add_FiresFeatureAdded()
    {
        var sut = new InMemoryAssetProvider();
        GeoFeature? raised = null;
        sut.FeatureAdded += (_, f) => raised = f;
        sut.Add(Feature("a"));
        raised!.Id.Should().Be("a");
    }

    [Fact]
    public void Add_FiresCollectionChanged()
    {
        var sut = new InMemoryAssetProvider();
        var fired = false;
        sut.CollectionChanged += (_, _) => fired = true;
        sut.Add(Feature("a"));
        fired.Should().BeTrue();
    }

    // ── Update ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_ReplacesFeature()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(new GeoFeature { Id = "a", Properties = { Name = "Old" } });
        sut.Update(new GeoFeature { Id = "a", Properties = { Name = "New" } });
        sut.GetById("a")!.Properties.Name.Should().Be("New");
    }

    [Fact]
    public void Update_SetsUpdatedAt()
    {
        var sut = new InMemoryAssetProvider();
        var feature = new GeoFeature { Id = "a" };
        feature.Properties.UpdatedAt = DateTime.UtcNow.AddDays(-1);
        sut.Add(feature);
        var before = DateTime.UtcNow;
        sut.Update(feature);
        sut.GetById("a")!.Properties.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void Update_FiresFeatureUpdated()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(Feature("a"));
        GeoFeature? raised = null;
        sut.FeatureUpdated += (_, f) => raised = f;
        sut.Update(Feature("a"));
        raised!.Id.Should().Be("a");
    }

    [Fact]
    public void Update_FiresCollectionChanged()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(Feature("a"));
        var fired = false;
        sut.CollectionChanged += (_, _) => fired = true;
        sut.Update(Feature("a"));
        fired.Should().BeTrue();
    }

    // ── AddRange ───────────────────────────────────────────────────────────────

    [Fact]
    public void AddRange_NewFeatures_AreAdded()
    {
        var sut = new InMemoryAssetProvider();
        sut.AddRange([Feature("a"), Feature("b")]);
        sut.GetAll().Select(f => f.Id).Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void AddRange_ExistingFeature_SetsUpdatedAt()
    {
        var sut = new InMemoryAssetProvider();
        var feature = new GeoFeature { Id = "a" };
        feature.Properties.UpdatedAt = DateTime.UtcNow.AddDays(-1);
        sut.Add(feature);
        var before = DateTime.UtcNow;
        sut.AddRange([feature]);
        sut.GetById("a")!.Properties.UpdatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void AddRange_FiresCollectionChangedExactlyOnce()
    {
        var sut = new InMemoryAssetProvider();
        var count = 0;
        sut.CollectionChanged += (_, _) => count++;
        sut.AddRange([Feature("a"), Feature("b"), Feature("c")]);
        count.Should().Be(1);
    }

    [Fact]
    public void AddRange_EmptyEnumerable_FiresCollectionChanged()
    {
        var sut = new InMemoryAssetProvider();
        var fired = false;
        sut.CollectionChanged += (_, _) => fired = true;
        sut.AddRange([]);
        fired.Should().BeTrue();
    }

    // ── Delete ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Delete_ExistingFeature_RemovesIt()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(Feature("a"));
        sut.Delete("a");
        sut.GetById("a").Should().BeNull();
    }

    [Fact]
    public void Delete_ExistingFeature_FiresFeatureDeleted()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(Feature("a"));
        string? deletedId = null;
        sut.FeatureDeleted += (_, id) => deletedId = id;
        sut.Delete("a");
        deletedId.Should().Be("a");
    }

    [Fact]
    public void Delete_ExistingFeature_FiresCollectionChanged()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(Feature("a"));
        var fired = false;
        sut.CollectionChanged += (_, _) => fired = true;
        sut.Delete("a");
        fired.Should().BeTrue();
    }

    [Fact]
    public void Delete_NonExistingId_DoesNotFireEvents()
    {
        var sut = new InMemoryAssetProvider();
        var deleted = false;
        var changed = false;
        sut.FeatureDeleted += (_, _) => deleted = true;
        sut.CollectionChanged += (_, _) => changed = true;
        sut.Delete("z");
        deleted.Should().BeFalse();
        changed.Should().BeFalse();
    }

    // ── Clear ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Clear_RemovesAllFeatures()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(Feature("a"));
        sut.Add(Feature("b"));
        sut.Clear();
        sut.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Clear_FiresCollectionChanged()
    {
        var sut = new InMemoryAssetProvider();
        var fired = false;
        sut.CollectionChanged += (_, _) => fired = true;
        sut.Clear();
        fired.Should().BeTrue();
    }

    // ── LoadAll ────────────────────────────────────────────────────────────────

    [Fact]
    public void LoadAll_ReplacesExistingFeatures()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(Feature("old"));
        sut.LoadAll([Feature("a"), Feature("b")]);
        sut.GetAll().Select(f => f.Id).Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void LoadAll_FiresCollectionChanged()
    {
        var sut = new InMemoryAssetProvider();
        var fired = false;
        sut.CollectionChanged += (_, _) => fired = true;
        sut.LoadAll([]);
        fired.Should().BeTrue();
    }

    // ── GetAssetTypes ──────────────────────────────────────────────────────────

    [Fact]
    public void GetAssetTypes_NewInstance_ContainsThreeDefaults()
    {
        new InMemoryAssetProvider().GetAssetTypes().Should().HaveCount(3);
    }

    // ── AddAssetType ───────────────────────────────────────────────────────────

    [Fact]
    public void AddAssetType_NewId_IsAdded()
    {
        var sut = new InMemoryAssetProvider();
        sut.AddAssetType(new AssetType { Id = Guid.NewGuid(), Name = "Custom" });
        sut.GetAssetTypes().Should().HaveCount(4);
    }

    [Fact]
    public void AddAssetType_DuplicateId_NotAddedAgain()
    {
        var sut = new InMemoryAssetProvider();
        var id = Guid.NewGuid();
        sut.AddAssetType(new AssetType { Id = id, Name = "First" });
        sut.AddAssetType(new AssetType { Id = id, Name = "Duplicate" });
        sut.GetAssetTypes().Should().HaveCount(4);
    }

    // ── DeleteAssetType ────────────────────────────────────────────────────────

    [Fact]
    public void DeleteAssetType_NonBuiltIn_IsRemoved()
    {
        var sut = new InMemoryAssetProvider();
        var id = Guid.NewGuid();
        sut.AddAssetType(new AssetType { Id = id, Name = "Custom", IsBuiltIn = false });
        sut.DeleteAssetType(id);
        sut.GetAssetTypes().Should().HaveCount(3);
    }

    [Fact]
    public void DeleteAssetType_BuiltIn_IsNotRemoved()
    {
        var sut = new InMemoryAssetProvider();
        sut.DeleteAssetType(AssetType.Point.Id);
        sut.GetAssetTypes().Should().HaveCount(3);
    }

    [Fact]
    public void DeleteAssetType_UnknownId_NoEffect()
    {
        var sut = new InMemoryAssetProvider();
        sut.DeleteAssetType(Guid.NewGuid());
        sut.GetAssetTypes().Should().HaveCount(3);
    }

    // ── GetWithin ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetWithin_NullGeometry_Excluded()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(new GeoFeature { Id = "a", Geometry = null });
        sut.GetWithin(BigBox()).Should().BeEmpty();
    }

    [Fact]
    public void GetWithin_PointInsideBounds_Included()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(FeatureAt("a", 0, 0));
        sut.GetWithin(BigBox()).Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public void GetWithin_PointOutsideBounds_Excluded()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(FeatureAt("a", 50, 50));
        sut.GetWithin(BigBox()).Should().BeEmpty();
    }

    // ── GetIntersecting ───────────────────────────────────────────────────────

    [Fact]
    public void GetIntersecting_NullGeometry_Excluded()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(new GeoFeature { Id = "a", Geometry = null });
        sut.GetIntersecting(BigBox()).Should().BeEmpty();
    }

    [Fact]
    public void GetIntersecting_PointInsideBox_Included()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(FeatureAt("a", 0, 0));
        sut.GetIntersecting(BigBox()).Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public void GetIntersecting_PointOutsideBox_Excluded()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(FeatureAt("a", 50, 50));
        sut.GetIntersecting(BigBox()).Should().BeEmpty();
    }

    // ── GetInBoundsAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetInBoundsAsync_PointInsideBbox_Included()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(FeatureAt("a", 0, 0));
        var result = await sut.GetInBoundsAsync(-10, -10, 10, 10);
        result.Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public async Task GetInBoundsAsync_PointOutsideBbox_Excluded()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(FeatureAt("a", 50, 50));
        var result = await sut.GetInBoundsAsync(-10, -10, 10, 10);
        result.Should().BeEmpty();
    }

    // ── GetInBoundsJsonAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetInBoundsJsonAsync_PointInsideBbox_ReturnsJsonObject()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(FeatureAt("a", 0, 0));
        var result = await sut.GetInBoundsJsonAsync(-10, -10, 10, 10);
        result.Should().ContainSingle();
        result[0].ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task GetInBoundsJsonAsync_NoFeaturesInBbox_ReturnsEmpty()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(FeatureAt("a", 50, 50));
        var result = await sut.GetInBoundsJsonAsync(-10, -10, 10, 10);
        result.Should().BeEmpty();
    }

    // ── GetNearby ─────────────────────────────────────────────────────────────

    [Fact]
    public void GetNearby_NullGeometry_Excluded()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(new GeoFeature { Id = "a", Geometry = null });
        sut.GetNearby(new GeoPoint(0, 0), 10).Should().BeEmpty();
    }

    [Fact]
    public void GetNearby_FeatureWithinDistance_Included()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(FeatureAt("a", 0.5, 0));
        sut.GetNearby(new GeoPoint(0, 0), 1.0).Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public void GetNearby_FeatureOutsideDistance_Excluded()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(FeatureAt("a", 5, 0));
        sut.GetNearby(new GeoPoint(0, 0), 1.0).Should().BeEmpty();
    }

    [Fact]
    public void GetNearby_MultipleFeatures_OrderedByAscendingDistance()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(FeatureAt("far", 0.8, 0));
        sut.Add(FeatureAt("near", 0.2, 0));
        var result = sut.GetNearby(new GeoPoint(0, 0), 2.0);
        result.Select(f => f.Id).Should().Equal("near", "far");
    }

    // ── Topology delegation ───────────────────────────────────────────────────

    [Fact]
    public void GetNeighbors_DelegatesToTopoGraph()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(TopoFeature("a", "b"));
        sut.Add(TopoFeature("b"));
        sut.GetNeighbors("a").Should().ContainSingle().Which.Id.Should().Be("b");
    }

    [Fact]
    public void GetDescendants_DelegatesToTopoGraph()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(TopoFeature("a", "b"));
        sut.Add(TopoFeature("b", "c"));
        sut.Add(TopoFeature("c"));
        sut.GetDescendants("a").Select(f => f.Id).Should().BeEquivalentTo(["b", "c"]);
    }

    [Fact]
    public void GetAncestors_DelegatesToTopoGraph()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(TopoFeature("a", "b"));
        sut.Add(TopoFeature("b"));
        sut.GetAncestors("b").Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public void FindPath_DelegatesToTopoGraph()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(TopoFeature("a", "b"));
        sut.Add(TopoFeature("b"));
        sut.FindPath("a", "b").Select(f => f.Id).Should().Equal("a", "b");
    }

    [Fact]
    public void FindShortestPath_DelegatesToTopoGraph()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(new GeoFeature { Id = "a", Topology = [new TopoEdge { TargetId = "b", Weight = 1.0 }] });
        sut.Add(TopoFeature("b"));
        sut.FindShortestPath("a", "b").Select(f => f.Id).Should().Equal("a", "b");
    }

    [Fact]
    public void GetConnectedComponents_DelegatesToTopoGraph()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(TopoFeature("a", "b"));
        sut.Add(TopoFeature("b"));
        sut.Add(TopoFeature("c"));
        sut.GetConnectedComponents().Should().HaveCount(2);
    }

    [Fact]
    public void HasCycles_NoCycle_ReturnsFalse()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(TopoFeature("a", "b"));
        sut.Add(TopoFeature("b"));
        sut.HasCycles().Should().BeFalse();
    }

    [Fact]
    public void HasCycles_WithCycle_ReturnsTrue()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(TopoFeature("a", "b"));
        sut.Add(TopoFeature("b", "a"));
        sut.HasCycles().Should().BeTrue();
    }

    [Fact]
    public void TopologicalSort_DelegatesToTopoGraph()
    {
        var sut = new InMemoryAssetProvider();
        sut.Add(TopoFeature("a", "b"));
        sut.Add(TopoFeature("b"));
        var result = sut.TopologicalSort();
        var ids = result.Select(f => f.Id).ToList();
        ids.IndexOf("a").Should().BeLessThan(ids.IndexOf("b"));
    }
}
