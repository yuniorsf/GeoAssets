using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using Xunit;
using static GeoAssets.Provider.PostgreSQL.Tests.QueryModeHarness;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// The two read modes of an organization's catalog (XD01-449) against the collection's PostGIS instance: the
/// mode decision, parity of every read between a store-served and a fully cached catalog, the enumeration limit,
/// writes and the by-id cache. Each test uses its own organizations, ids, asset type and map area.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrganizationCatalogQueryModeTests(PostgresContainerFixture fixture)
{
    private const int FullCache = 1_000_000;

    private readonly QueryModeHarness _harness = new(fixture);

    private sealed record Parity(QueryWorld World, string[] Own, string[] Shared, string[] Foreign);

    /// <summary>Six own, three shared and four foreign features, spread along the world's diagonal; tokens vary by feature.</summary>
    private async Task<Parity> SeedParityAsync()
    {
        var world = NewWorld();
        var own = Enumerable.Range(0, 6).Select(_ => NewId("own")).ToArray();
        var shared = Enumerable.Range(0, 3).Select(_ => NewId("shr")).ToArray();
        var foreign = Enumerable.Range(0, 4).Select(_ => NewId("frg")).ToArray();
        var features = new List<GeoFeature>();
        var index = 0;
        foreach (var (ids, organization) in new[] { (own, world.Org), (shared, Guid.Empty), (foreign, world.Other) })
        {
            foreach (var id in ids)
            {
                var feature = Feature(world, id, organization, 0.001 * (++index));
                if (index % 2 == 0)
                    feature.Properties.Description = $"about {world.DescriptionToken}";
                if (index % 3 == 0)
                    feature.Properties.CustomAttributes[$"{world.KeyToken}-k"] = "x";
                if (index % 4 == 0)
                    feature.Properties.CustomAttributes["note"] = $"has {world.ValueToken}";
                features.Add(feature);
            }
        }

        await _harness.SeedAsync([.. features]);
        return new Parity(world, own, shared, foreign);
    }

    // ── Mode decision ─────────────────────────────────────────────────────────

    [Fact]
    public async Task WarmAsync_PicksQueryMode_OnlyWhenTheOrganizationHasMoreOwnRowsThanTheThreshold()
    {
        var big = NewWorld();
        var small = NewWorld();
        await _harness.SeedAsync(
            Feature(big, NewId("b"), big.Org, 0.001), Feature(big, NewId("b"), big.Org, 0.002),
            Feature(big, NewId("b"), big.Org, 0.003), Feature(big, NewId("b"), big.Org, 0.004),
            Feature(small, NewId("s"), small.Org, 0.001), Feature(small, NewId("s"), small.Org, 0.002),
            Feature(small, NewId("s"), small.Org, 0.003));
        await using var catalogs = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 3);
        var unknown = Guid.NewGuid();

        catalogs.Sut.IsQueryBacked(big.Org).Should().BeFalse("nothing is decided before a warm");
        await catalogs.Sut.WarmAsync(big.Org, CancellationToken.None);
        await catalogs.Sut.WarmAsync(small.Org, CancellationToken.None);
        await catalogs.Sut.WarmAsync(Guid.Empty, CancellationToken.None);

        catalogs.Sut.IsQueryBacked(big.Org).Should().BeTrue();
        catalogs.Sut.IsQueryBacked(small.Org).Should().BeFalse("exactly at the threshold stays fully cached");
        catalogs.Sut.IsQueryBacked(Guid.Empty).Should().BeFalse();
        catalogs.Sut.IsQueryBacked(unknown).Should().BeFalse();
    }

    [Fact]
    public async Task AThresholdOfZero_ServesEveryOrganizationWithRowsFromTheStore()
    {
        var world = NewWorld();
        await _harness.SeedAsync(Feature(world, NewId("z"), world.Org, 0.001));
        await using var catalogs = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 0);

        await catalogs.Sut.WarmAsync(world.Org, CancellationToken.None);

        catalogs.Sut.IsQueryBacked(world.Org).Should().BeTrue();
    }

    [Fact]
    public async Task AFirstReadWithoutAWarm_OfALargeOrganization_LeavesItQueryBacked_AndServesTheRead()
    {
        var world = NewWorld();
        var id = NewId("first");
        await _harness.SeedAsync(
            Feature(world, id, world.Org, 0.001), Feature(world, NewId("f"), world.Org, 0.002), Feature(world, NewId("f"), world.Org, 0.003));
        await using var catalogs = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 2);

        var found = catalogs.For(world.Org).GetById(id);

        found.Should().NotBeNull();
        catalogs.Sut.IsQueryBacked(world.Org).Should().BeTrue();
    }

    [Fact]
    public async Task RefreshAsync_SwitchesFullCacheToQuery_WhenTheOrganizationGrows_AndBackWhenItShrinks()
    {
        var world = NewWorld();
        var first = NewId("r");
        var second = NewId("r");
        await _harness.SeedAsync(Feature(world, first, world.Org, 0.001), Feature(world, second, world.Org, 0.002));
        await using var catalogs = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 3);
        await catalogs.Sut.WarmAsync(world.Org, CancellationToken.None);
        catalogs.Sut.IsQueryBacked(world.Org).Should().BeFalse();
        var view = catalogs.For(world.Org);

        var more = new[] { NewId("r"), NewId("r"), NewId("r") };
        await _harness.SeedAsync([.. more.Select((id, i) => Feature(world, id, world.Org, 0.003 + (0.001 * i)))]);
        await catalogs.Sut.RefreshAsync(world.Org, CancellationToken.None);

        catalogs.Sut.IsQueryBacked(world.Org).Should().BeTrue();
        view.GetById(more[0]).Should().NotBeNull();
        Ids(view.GetByAssetType(world.AssetType)).Should().BeEquivalentTo([first, second, .. more]);

        await _harness.DeleteDirectAsync(more);
        await catalogs.Sut.RefreshAsync(world.Org, CancellationToken.None);

        catalogs.Sut.IsQueryBacked(world.Org).Should().BeFalse();
        view.GetById(more[0]).Should().BeNull();
        Ids(view.GetByAssetType(world.AssetType)).Should().BeEquivalentTo([first, second]);
    }

    // ── Parity ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EveryRead_OfAQueryBackedCatalog_ReturnsTheSameFeaturesAsTheFullCache_NeverAnotherOrganizations()
    {
        var seeded = await SeedParityAsync();
        var world = seeded.World;
        await using var full = await _harness.NewCatalogsAsync(FullCache);
        await using var query = await _harness.NewCatalogsAsync(0);
        await full.Sut.WarmAsync(world.Org, CancellationToken.None);
        await query.Sut.WarmAsync(world.Org, CancellationToken.None);
        full.Sut.IsQueryBacked(world.Org).Should().BeFalse();
        query.Sut.IsQueryBacked(world.Org).Should().BeTrue();
        var fullView = full.For(world.Org);
        var queryView = query.For(world.Org);
        string[] visible = [.. seeded.Own, .. seeded.Shared];

        foreach (var id in visible)
            Ids([queryView.GetById(id)!]).Should().Equal(Ids([fullView.GetById(id)!]));
        foreach (var id in seeded.Foreign)
            queryView.GetById(id).Should().BeNull("another organization's feature is not in the view");
        queryView.GetById(NewId("missing")).Should().BeNull();

        var all = Ids(queryView.GetAll()).ToList();
        all.Should().Contain(visible).And.NotContain(seeded.Foreign);
        all.Should().BeEquivalentTo(Ids(fullView.GetAll()), "the same own and shared features");

        void SameSet(Func<GeoAssets.Core.Interfaces.IAssetProvider, IEnumerable<GeoFeature>> read, string[]? expected = null)
        {
            var fromQuery = Ids(read(queryView)).ToList();
            fromQuery.Should().BeEquivalentTo(Ids(read(fullView)));
            fromQuery.Should().NotContain(seeded.Foreign);
            if (expected is not null)
                fromQuery.Should().BeEquivalentTo(expected);
        }

        SameSet(p => p.GetByAssetType(world.AssetType), visible);
        SameSet(p => p.GetByAssetType($"none-{world.AssetType}"), []);
        SameSet(p => p.Search(world.NameToken), visible);
        SameSet(p => p.Search(world.DescriptionToken));
        SameSet(p => p.Search(world.KeyToken));
        SameSet(p => p.Search(world.ValueToken));
        SameSet(p => p.GetWithin(Around(world)), visible);
        SameSet(p => p.GetIntersecting(Around(world)), visible);
        SameSet(p => p.GetWithin(Around(world, 0.0045)));
        SameSet(p => p.GetIntersecting(Around(world, 0.0045)));
        SameSet(p => p.GetNearby(new GeoPoint(world.Lon, world.Lat), 0.05), visible);
        SameSet(p => p.GetNearby(new GeoPoint(world.Lon, world.Lat), 0.004));
        Ids(queryView.Search(world.DescriptionToken)).Should().NotBeEmpty();
        Ids(queryView.Search(world.KeyToken)).Should().NotBeEmpty();
        Ids(queryView.Search(world.ValueToken)).Should().NotBeEmpty();
    }

    [Fact]
    public async Task AQueryBackedCatalog_ReturnsOwnFeaturesInOrdinalIdOrder_AndNearbyByDistance()
    {
        var seeded = await SeedParityAsync();
        var world = seeded.World;
        await using var query = await _harness.NewCatalogsAsync(0);
        await query.Sut.WarmAsync(world.Org, CancellationToken.None);
        var view = query.For(world.Org);

        var byType = Ids(view.GetByAssetType(world.AssetType)).Where(seeded.Own.Contains).ToList();
        var within = Ids(view.GetWithin(Around(world))).Where(seeded.Own.Contains).ToList();
        var nearby = Ids(view.GetNearby(new GeoPoint(world.Lon, world.Lat), 0.05)).ToList();

        byType.Should().Equal(Ordinal(seeded.Own));
        within.Should().Equal(Ordinal(seeded.Own));
        nearby.Where(seeded.Own.Contains).Should().Equal(seeded.Own, "own features were seeded farther and farther from the centre");
        nearby.Where(seeded.Shared.Contains).Should().Equal(seeded.Shared);
    }

    // ── Enumeration limit ─────────────────────────────────────────────────────

    [Fact]
    public async Task EnumeratingReads_AreRefusedBeyondTheLimit_AndServedAtIt()
    {
        var world = NewWorld();
        await _harness.SeedAsync([.. Enumerable.Range(0, 5).Select(i => Feature(world, NewId("e"), world.Org, 0.001 * (i + 1)))]);
        await using var refused = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 0, enumerationLimit: 4);
        await using var served = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 0, enumerationLimit: 5);
        var strict = refused.For(world.Org);
        var exact = served.For(world.Org);

        var getAll = () => strict.GetAll();
        var byType = () => strict.GetByAssetType(world.AssetType);
        var within = () => strict.GetWithin(Around(world));

        getAll.Should().Throw<CatalogEnumerationLimitException>().Which.Should().Match<CatalogEnumerationLimitException>(
            e => e.Operation == "GetAll" && e.Limit == 4);
        byType.Should().Throw<CatalogEnumerationLimitException>().Which.Should().Match<CatalogEnumerationLimitException>(
            e => e.Operation == "GetByAssetType" && e.Limit == 4);
        within.Should().Throw<CatalogEnumerationLimitException>().Which.Should().Match<CatalogEnumerationLimitException>(
            e => e.Operation == "GetWithin" && e.Limit == 4);
        exact.GetByAssetType(world.AssetType).Should().HaveCount(5);
        exact.GetWithin(Around(world)).Should().HaveCount(5);
        exact.GetAll().Should().HaveCountGreaterThanOrEqualTo(5);
    }

    // ── Writes in query mode ──────────────────────────────────────────────────

    [Fact]
    public async Task Writes_InQueryMode_AreSeenByTheNextReads_AndRaiseTheEvents()
    {
        var world = NewWorld();
        var existing = NewId("w");
        var shared = NewId("w-shared");
        await _harness.SeedAsync(
            Feature(world, existing, world.Org, 0.001), Feature(world, NewId("w"), world.Org, 0.002),
            Feature(world, shared, Guid.Empty, 0.003));
        await using var catalogs = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 0);
        await catalogs.Sut.WarmAsync(world.Org, CancellationToken.None);
        var view = catalogs.For(world.Org);
        var added = new List<string>();
        var updated = new List<string>();
        var deleted = new List<string>();
        view.FeatureAdded += (_, f) => added.Add(f.Id);
        view.FeatureUpdated += (_, f) => updated.Add(f.Id);
        view.FeatureDeleted += (_, id) => deleted.Add(id);

        var created = NewId("w-new");
        view.Add(Feature(world, created, world.Org, 0.004));
        view.GetById(created).Should().NotBeNull();
        Ids(view.GetByAssetType(world.AssetType)).Should().Contain(created);
        (await _harness.RowAsync(created))!.OrganizationId.Should().Be(world.Org);

        view.GetById(existing)!.Properties.Name.Should().Contain(existing, "this read fills the by-id cache");
        var renamed = view.GetById(existing)!;
        renamed.Properties.Name = $"{world.NameToken} renamed";
        view.Update(renamed);
        view.GetById(existing)!.Properties.Name.Should().Be($"{world.NameToken} renamed", "an update evicts the by-id cache");
        view.GetAll().Single(f => string.Equals(f.Id, existing, StringComparison.Ordinal)).Properties.Name.Should().Be($"{world.NameToken} renamed");

        var sharedFeature = view.GetById(shared)!;
        sharedFeature.Properties.Name = $"{world.NameToken} shared renamed";
        sharedFeature.Properties.OrganizationId = world.Org;
        view.Update(sharedFeature);
        (await _harness.RowAsync(shared))!.Should().Be(new QueryRowSnapshot($"{world.NameToken} shared renamed", Guid.Empty));

        view.Delete(created);
        view.GetById(created).Should().BeNull("a delete evicts the by-id cache");
        Ids(view.GetAll()).Should().NotContain(created);
        (await _harness.RowAsync(created)).Should().BeNull();

        added.Should().Equal(created);
        updated.Should().Equal(existing, shared);
        deleted.Should().Equal(created);
    }

    [Fact]
    public async Task AWriteNamingAnotherOrganizationsFeature_IsRefusedInQueryMode()
    {
        var world = NewWorld();
        var own = NewId("x-own");
        var foreign = NewId("x-frg");
        await _harness.SeedAsync(Feature(world, own, world.Org, 0.001), Feature(world, foreign, world.Other, 0.002));
        await using var catalogs = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 0);
        var view = catalogs.For(world.Org);
        var hijack = Feature(world, foreign, world.Org, 0.05);
        hijack.Properties.Name = "hijacked";

        var update = () => view.Update(hijack);
        var addRange = () => view.AddRange([Feature(world, NewId("x-new"), world.Org, 0.003), hijack]);

        update.Should().Throw<ForeignGeoFeatureWriteException>().Which.FeatureIds.Should().Equal(foreign);
        addRange.Should().Throw<ForeignGeoFeatureWriteException>().Which.FeatureIds.Should().Equal(foreign);
        (await _harness.RowAsync(foreign)).Should().Be(new QueryRowSnapshot($"{world.NameToken} {foreign}", world.Other));
        view.GetById(foreign).Should().BeNull();
    }

    // ── By-id cache ───────────────────────────────────────────────────────────

    [Fact]
    public async Task WithoutAByIdCache_ReadsStillWork_AndSeeTheStore()
    {
        var world = NewWorld();
        var id = NewId("nc");
        await _harness.SeedAsync(Feature(world, id, world.Org, 0.001), Feature(world, NewId("nc"), world.Org, 0.002));
        await using var catalogs = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 0, byIdCacheCapacity: 0);
        var view = catalogs.For(world.Org);

        view.GetById(id).Should().NotBeNull();
        await _harness.RenameDirectAsync(id, "changed elsewhere");

        view.GetById(id)!.Properties.Name.Should().Be("changed elsewhere");
        catalogs.Sut.IsQueryBacked(world.Org).Should().BeTrue();
    }

    [Fact]
    public async Task RefreshAsync_ClearsTheByIdCache_SoARowChangedElsewhereIsSeen()
    {
        var world = NewWorld();
        var id = NewId("rc");
        await _harness.SeedAsync(Feature(world, id, world.Org, 0.001), Feature(world, NewId("rc"), world.Org, 0.002));
        await using var catalogs = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 0, byIdCacheCapacity: 10);
        await catalogs.Sut.WarmAsync(world.Org, CancellationToken.None);
        var view = catalogs.For(world.Org);
        view.GetById(id)!.Properties.Name.Should().Contain(id);

        await _harness.RenameDirectAsync(id, "changed elsewhere");
        await catalogs.Sut.RefreshAsync(world.Org, CancellationToken.None);

        view.GetById(id)!.Properties.Name.Should().Be("changed elsewhere");
        catalogs.Sut.IsQueryBacked(world.Org).Should().BeTrue();
    }
}
