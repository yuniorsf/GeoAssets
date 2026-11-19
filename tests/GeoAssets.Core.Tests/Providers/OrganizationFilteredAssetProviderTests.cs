using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Providers;

public sealed class OrganizationFilteredAssetProviderTests
{
    private static readonly Guid OrgA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OrgB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly TestAssetProvider _inner = new();

    public OrganizationFilteredAssetProviderTests()
    {
        _inner.Add(Feature("a1", OrgA, 0.001, "alpha"));
        _inner.Add(Feature("b1", OrgB, 0.002, "alpha"));
        _inner.Add(Feature("s1", Guid.Empty, 0.003, "alpha"));
    }

    private static GeoFeature Feature(string id, Guid organizationId, double offset, string name, params string[] edgesTo) => new()
    {
        Id = id,
        Geometry = new GeoPoint(offset, offset),
        Topology = [.. edgesTo.Select(target => new TopoEdge { TargetId = target, Kind = "feeds" })],
        Properties = new GeoFeatureProperties { Name = $"{name} {id}", OrganizationId = organizationId },
    };

    private OrganizationFilteredAssetProvider ViewOf(Guid organizationId) => new(organizationId, _inner);

    private static string[] Ids(IEnumerable<GeoFeature> features) => [.. features.Select(f => f.Id).Order(StringComparer.Ordinal)];

    private static GeoPolygon Everything() => new([(-1, -1), (1, -1), (1, 1), (-1, 1), (-1, -1)]);

    [Fact]
    public void Constructor_NullInner_Throws()
    {
        var act = () => new OrganizationFilteredAssetProvider(OrgA, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void OrganizationId_IsTheViewsOrganization() => ViewOf(OrgA).OrganizationId.Should().Be(OrgA);

    [Fact]
    public void UnwrapToConcrete_ReachesTheInnerProvider() => ViewOf(OrgA).UnwrapToConcrete().Should().BeSameAs(_inner);

    // ── Reads ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("a1", true)]
    [InlineData("s1", true)]
    [InlineData("b1", false)]
    [InlineData("missing", false)]
    public void GetById_OwnAndSharedVisible_ForeignAndMissingNull(string id, bool visible) =>
        (ViewOf(OrgA).GetById(id) is not null).Should().Be(visible);

    [Fact]
    public void GetAll_OwnPlusShared_NotForeign() => Ids(ViewOf(OrgA).GetAll()).Should().Equal("a1", "s1");

    [Fact]
    public void GetAll_ForTheOtherOrganization_IsTheMirrorImage() => Ids(ViewOf(OrgB).GetAll()).Should().Equal("b1", "s1");

    [Fact]
    public void GetAll_EmptyView_IsSharedOnly() => Ids(ViewOf(Guid.Empty).GetAll()).Should().Equal("s1");

    [Fact]
    public void GetByAssetType_ExcludesForeign()
    {
        var assetType = AssetType.Point.Id.ToString();

        Ids(ViewOf(OrgA).GetByAssetType(assetType)).Should().Equal("a1", "s1");
    }

    [Fact]
    public void Search_ExcludesForeign() => Ids(ViewOf(OrgA).Search("alpha")).Should().Equal("a1", "s1");

    [Fact]
    public void GetWithin_ExcludesForeign() => Ids(ViewOf(OrgA).GetWithin(Everything())).Should().Equal("a1", "s1");

    [Fact]
    public void GetIntersecting_ExcludesForeign() => Ids(ViewOf(OrgA).GetIntersecting(Everything())).Should().Equal("a1", "s1");

    [Fact]
    public async Task GetInBoundsAsync_ExcludesForeign()
    {
        var features = await ViewOf(OrgA).GetInBoundsAsync(-1, -1, 1, 1);

        Ids(features).Should().Equal("a1", "s1");
    }

    [Fact]
    public async Task GetInBoundsJsonAsync_ExcludesForeign()
    {
        var elements = await ViewOf(OrgA).GetInBoundsJsonAsync(-1, -1, 1, 1);

        var ids = elements.Select(e => e.GetProperty("id").GetString()).Order(StringComparer.Ordinal);
        ids.Should().Equal("a1", "s1");
    }

    [Fact]
    public async Task GetInBoundsRawJsonAsync_IsNullEvenWhenTheInnerOffersIt()
    {
        _inner.RawJsonResult = "[{\"id\":\"b1\"}]";

        var raw = await ((IAssetProvider)ViewOf(OrgA)).GetInBoundsRawJsonAsync(-1, -1, 1, 1);

        raw.Should().BeNull();
    }

    [Fact]
    public void GetNearby_ExcludesForeign() =>
        Ids(ViewOf(OrgA).GetNearby(new GeoPoint(0, 0), 1)).Should().Equal("a1", "s1");

    [Fact]
    public async Task GetPageAsync_TotalsCountOnlyVisibleFeatures()
    {
        var page = await ((IAssetProvider)ViewOf(OrgA)).GetPageAsync(new AssetQuery { Take = 1 });

        page.TotalCount.Should().Be(2);
        page.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task GetPageAsync_QueryingAForeignOrganization_FindsNothing()
    {
        var page = await ((IAssetProvider)ViewOf(OrgA)).GetPageAsync(new AssetQuery { OrganizationId = OrgB });

        page.TotalCount.Should().Be(0);
        page.Items.Should().BeEmpty();
    }

    [Fact]
    public void Topology_DoesNotReachForeignFeatures()
    {
        _inner.Update(Feature("a1", OrgA, 0.001, "alpha", "b1", "s1"));
        var view = (IAssetProvider)ViewOf(OrgA);

        Ids(view.GetNeighbors("a1")).Should().Equal("s1");
        Ids(view.GetDescendants("a1")).Should().Equal("s1");
    }

    // ── Asset types ────────────────────────────────────────────────────────────

    [Fact]
    public void GetAssetTypes_OwnAndSharedOnly()
    {
        var own = new AssetType { Id = Guid.NewGuid(), Name = "own", OrganizationId = OrgA };
        var foreign = new AssetType { Id = Guid.NewGuid(), Name = "foreign", OrganizationId = OrgB };
        _inner.AddAssetType(own);
        _inner.AddAssetType(foreign);

        var ids = ViewOf(OrgA).GetAssetTypes().Select(t => t.Id).ToList();

        ids.Should().Contain(own.Id).And.NotContain(foreign.Id);
        ids.Should().Contain(AssetType.Point.Id);
    }

    [Fact]
    public void DeleteAssetType_Foreign_DoesNothing()
    {
        var foreign = new AssetType { Id = Guid.NewGuid(), Name = "foreign", OrganizationId = OrgB };
        _inner.AddAssetType(foreign);

        ViewOf(OrgA).DeleteAssetType(foreign.Id);

        _inner.GetAssetTypes().Should().Contain(t => t.Id == foreign.Id);
    }

    [Fact]
    public void DeleteAssetType_Own_Deletes()
    {
        var own = new AssetType { Id = Guid.NewGuid(), Name = "own", OrganizationId = OrgA };
        _inner.AddAssetType(own);

        ViewOf(OrgA).DeleteAssetType(own.Id);

        _inner.GetAssetTypes().Should().NotContain(t => t.Id == own.Id);
    }

    [Fact]
    public void AddAssetType_PassesThrough()
    {
        var type = new AssetType { Id = Guid.NewGuid(), Name = "new", OrganizationId = OrgA };

        ViewOf(OrgA).AddAssetType(type);

        _inner.GetAssetTypes().Should().Contain(t => t.Id == type.Id);
    }

    // ── Layers and rules pass through ──────────────────────────────────────────

    [Fact]
    public void LayersAndRules_PassThroughToTheInner()
    {
        var view = ViewOf(OrgA);
        var layer = new Layer { Id = Guid.NewGuid() };
        var rule = new LayerRule { Id = Guid.NewGuid(), AssetTypeId = AssetType.Point.Id };

        view.AddLayer(layer);
        view.AddLayerRule(rule);

        _inner.GetLayers().Should().Contain(l => l.Id == layer.Id);
        view.GetLayers().Should().Contain(l => l.Id == layer.Id);
        view.GetLayerRules(AssetType.Point.Id).Should().Contain(r => r.Id == rule.Id);

        view.DeleteLayerRule(rule.Id);
        view.DeleteLayer(layer.Id);

        _inner.GetLayers().Should().BeEmpty();
        _inner.GetLayerRules(AssetType.Point.Id).Should().BeEmpty();
    }

    // ── Writes ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_NewFeature_IsStampedWithTheViewsOrganization_EvenWhenItStatesAnother()
    {
        ViewOf(OrgA).Add(Feature("new", OrgB, 0.01, "n"));

        _inner.GetById("new")!.Properties.OrganizationId.Should().Be(OrgA);
    }

    [Fact]
    public void Add_NewFeatureWithoutOrganization_IsStamped()
    {
        ViewOf(OrgA).Add(Feature("new", Guid.Empty, 0.01, "n"));

        _inner.GetById("new")!.Properties.OrganizationId.Should().Be(OrgA);
    }

    [Fact]
    public void Add_Null_Throws() => ((Action)(() => ViewOf(OrgA).Add(null!))).Should().Throw<ArgumentNullException>();

    [Fact]
    public void Update_Null_Throws() => ((Action)(() => ViewOf(OrgA).Update(null!))).Should().Throw<ArgumentNullException>();

    [Fact]
    public void AddRange_Null_Throws() => ((Action)(() => ViewOf(OrgA).AddRange(null!))).Should().Throw<ArgumentNullException>();

    [Fact]
    public void LoadAll_Null_Throws() => ((Action)(() => ViewOf(OrgA).LoadAll(null!))).Should().Throw<ArgumentNullException>();

    [Fact]
    public void Update_OwnFeature_StaysOwn()
    {
        ViewOf(OrgA).Update(Feature("a1", OrgB, 0.001, "renamed"));

        var stored = _inner.GetById("a1")!;
        stored.Properties.OrganizationId.Should().Be(OrgA);
        stored.Properties.Name.Should().Be("renamed a1");
    }

    [Fact]
    public void Update_SharedFeature_StaysShared()
    {
        ViewOf(OrgA).Update(Feature("s1", OrgA, 0.003, "renamed"));

        var stored = _inner.GetById("s1")!;
        stored.Properties.OrganizationId.Should().Be(Guid.Empty);
        stored.Properties.Name.Should().Be("renamed s1");
    }

    [Fact]
    public void Update_ForeignFeature_IsRefusedAndNothingIsWritten()
    {
        var before = _inner.GetById("b1")!.Properties.Name;

        var act = () => ViewOf(OrgA).Update(Feature("b1", OrgA, 0.002, "hijacked"));

        act.Should().Throw<ForeignGeoFeatureWriteException>().Which.FeatureIds.Should().Equal("b1");
        var stored = _inner.GetById("b1")!;
        stored.Properties.OrganizationId.Should().Be(OrgB);
        stored.Properties.Name.Should().Be(before);
    }

    [Fact]
    public void Add_ForeignId_IsRefusedAndNothingIsWritten()
    {
        var before = _inner.GetById("b1")!.Properties.Name;

        var act = () => ViewOf(OrgA).Add(Feature("b1", OrgA, 0.002, "hijacked"));

        act.Should().Throw<ForeignGeoFeatureWriteException>();
        _inner.GetById("b1")!.Properties.Name.Should().Be(before);
    }

    [Fact]
    public void AddRange_WithOneForeignId_WritesNothingAtAll()
    {
        var act = () => ViewOf(OrgA).AddRange([Feature("fresh", OrgA, 0.01, "f"), Feature("b1", OrgA, 0.002, "hijacked")]);

        act.Should().Throw<ForeignGeoFeatureWriteException>().Which.FeatureIds.Should().Equal("b1");
        _inner.GetById("fresh").Should().BeNull();
        _inner.GetById("b1")!.Properties.OrganizationId.Should().Be(OrgB);
    }

    [Fact]
    public void AddRange_AllVisible_StampsEachByItsStoredOwner()
    {
        ViewOf(OrgA).AddRange([Feature("fresh", OrgB, 0.01, "f"), Feature("s1", OrgA, 0.003, "shared"), Feature("a1", OrgB, 0.001, "own")]);

        _inner.GetById("fresh")!.Properties.OrganizationId.Should().Be(OrgA);
        _inner.GetById("s1")!.Properties.OrganizationId.Should().Be(Guid.Empty);
        _inner.GetById("a1")!.Properties.OrganizationId.Should().Be(OrgA);
    }

    [Fact]
    public void EmptyView_Add_StampsEmpty()
    {
        ViewOf(Guid.Empty).Add(Feature("new", OrgA, 0.01, "n"));

        _inner.GetById("new")!.Properties.OrganizationId.Should().Be(Guid.Empty);
    }

    [Fact]
    public void Update_OnTheEmptyView_OfAnOrganizationsFeature_IsRefused()
    {
        var act = () => ViewOf(Guid.Empty).Update(Feature("a1", Guid.Empty, 0.001, "x"));

        act.Should().Throw<ForeignGeoFeatureWriteException>();
    }

    [Theory]
    [InlineData("a1", false)]
    [InlineData("s1", false)]
    [InlineData("b1", true)]
    [InlineData("missing", true)]
    public void Delete_DeletesVisibleFeatures_AndDoesNothingForInvisibleOnes(string id, bool survives)
    {
        var existed = _inner.GetById(id) is not null;

        ViewOf(OrgA).Delete(id);

        (_inner.GetById(id) is not null).Should().Be(existed && survives);
    }

    [Fact]
    public void Delete_Foreign_RaisesNoDeletedEvent()
    {
        var deleted = new List<string>();
        _inner.FeatureDeleted += (_, id) => deleted.Add(id);

        ViewOf(OrgA).Delete("b1");

        deleted.Should().BeEmpty();
    }

    [Fact]
    public void Clear_RemovesOnlyOwnFeatures()
    {
        ViewOf(OrgA).Clear();

        Ids(_inner.GetAll()).Should().Equal("b1", "s1");
    }

    [Fact]
    public void Clear_OnTheEmptyView_LeavesEverythingExceptTheSharedFeatures()
    {
        ViewOf(Guid.Empty).Clear();

        Ids(_inner.GetAll()).Should().Equal("a1", "b1");
    }

    [Fact]
    public void LoadAll_ReplacesOwnFeatures_AndLeavesOthersAlone()
    {
        ViewOf(OrgA).LoadAll([Feature("a2", OrgB, 0.01, "n")]);

        Ids(_inner.GetAll()).Should().Equal("a2", "b1", "s1");
        _inner.GetById("a2")!.Properties.OrganizationId.Should().Be(OrgA);
    }

    [Fact]
    public void LoadAll_WithAForeignId_IsRefusedAndClearsNothing()
    {
        var act = () => ViewOf(OrgA).LoadAll([Feature("b1", OrgA, 0.002, "x")]);

        act.Should().Throw<ForeignGeoFeatureWriteException>();
        Ids(_inner.GetAll()).Should().Equal("a1", "b1", "s1");
    }

    // ── Events ─────────────────────────────────────────────────────────────────

    [Fact]
    public void FeatureAdded_ReachesSubscribersOnlyForVisibleFeatures()
    {
        var seen = new List<string>();
        ViewOf(OrgA).FeatureAdded += (_, f) => seen.Add(f.Id);

        _inner.Add(Feature("a2", OrgA, 0.01, "n"));
        _inner.Add(Feature("b2", OrgB, 0.02, "n"));
        _inner.Add(Feature("s2", Guid.Empty, 0.03, "n"));

        seen.Should().Equal("a2", "s2");
    }

    [Fact]
    public void FeatureUpdated_ReachesSubscribersOnlyForVisibleFeatures()
    {
        var seen = new List<string>();
        ViewOf(OrgA).FeatureUpdated += (_, f) => seen.Add(f.Id);

        _inner.Update(Feature("a1", OrgA, 0.001, "x"));
        _inner.Update(Feature("b1", OrgB, 0.002, "x"));

        seen.Should().Equal("a1");
    }

    [Fact]
    public void FeatureAdded_Unsubscribe_StopsDelivery()
    {
        var view = ViewOf(OrgA);
        var count = 0;
        EventHandler<GeoFeature> handler = (_, _) => count++;
        view.FeatureAdded += handler;
        view.FeatureAdded -= handler;

        _inner.Add(Feature("a2", OrgA, 0.01, "n"));

        count.Should().Be(0);
    }

    [Fact]
    public void FeatureUpdated_Unsubscribe_StopsDelivery()
    {
        var view = ViewOf(OrgA);
        var count = 0;
        EventHandler<GeoFeature> handler = (_, _) => count++;
        view.FeatureUpdated += handler;
        view.FeatureUpdated -= handler;

        _inner.Update(Feature("a1", OrgA, 0.001, "x"));

        count.Should().Be(0);
    }

    [Fact]
    public void FeatureAddedAndUpdated_NullHandlers_AreIgnored()
    {
        var view = ViewOf(OrgA);

        view.FeatureAdded += null;
        view.FeatureAdded -= null;
        view.FeatureUpdated += null;
        view.FeatureUpdated -= null;

        _inner.Add(Feature("a2", OrgA, 0.01, "n"));
    }

    [Fact]
    public void FeatureDeletedAndCollectionChanged_PassThrough()
    {
        var view = ViewOf(OrgA);
        var deleted = new List<string>();
        var changes = 0;
        EventHandler<string> onDeleted = (_, id) => deleted.Add(id);
        EventHandler onChanged = (_, _) => changes++;
        view.FeatureDeleted += onDeleted;
        view.CollectionChanged += onChanged;

        _inner.Delete("b1");

        deleted.Should().Equal("b1");
        changes.Should().Be(1);

        view.FeatureDeleted -= onDeleted;
        view.CollectionChanged -= onChanged;
        _inner.Delete("a1");

        deleted.Should().Equal("b1");
        changes.Should().Be(1);
    }
}
