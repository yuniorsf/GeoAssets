using FluentAssertions;
using GeoAssets.Core.Rendering;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Provider.PostgreSQL.Tests.TestDoubles;
using Xunit;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// The PostGIS pushdown (<see cref="PostgresAssetRenderQuery"/>) and the generic query over the same organization's
/// catalog (<see cref="AssetProviderRenderQuery"/> over <see cref="PostgresOrganizationAssetCatalogs.For"/> — the
/// request's <c>IAssetProvider</c> in production) must paint and identify exactly the same features (XD01-372):
/// the same ids and colors for every box and asset type filter, the same feature info in the same order.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AssetRenderQueryParityTests(PostgresContainerFixture fixture)
{
    private static (PostgresAssetRenderQuery Pushdown, AssetProviderRenderQuery Generic) QueriesOf(
        RenderQueryWorld world, PostgresContainerFixture fixture)
    {
        var catalog = world.Catalogs.For(world.OrgA);
        return (
            new PostgresAssetRenderQuery(fixture.CreateOptions(), new FakeTenantContext(world.OrgA), catalog, TimeProvider.System),
            new AssetProviderRenderQuery(catalog));
    }

    private static (string Id, string Name, string AssetTypeId, string Color)[] Painted(IEnumerable<RenderableFeature> features) =>
        [.. features.Select(f => (f.Id, f.Name, f.AssetTypeId, f.Color)).OrderBy(f => f.Id, StringComparer.Ordinal)];

    [Fact]
    public async Task GetRenderableAsync_ForSeveralBoxes_PaintsTheSameFeaturesInTheSameColors()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var (pushdown, generic) = QueriesOf(world, fixture);

        foreach (var bbox in new[] { world.Whole, world.AroundPlain, world.Empty })
        {
            var fromPushdown = await pushdown.GetRenderableAsync(bbox, new RenderQueryOptions(), CancellationToken.None);
            var fromGeneric = await generic.GetRenderableAsync(bbox, new RenderQueryOptions(), CancellationToken.None);

            Painted(fromPushdown).Should().Equal(Painted(fromGeneric), "for {0}", bbox);
        }
    }

    [Fact]
    public async Task GetRenderableAsync_OverTheWholeArea_PaintsOrganizationAAndSharedFeatures_EachTierItsColor()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var (pushdown, generic) = QueriesOf(world, fixture);

        foreach (var query in new IAssetRenderQuery[] { pushdown, generic })
        {
            var features = await query.GetRenderableAsync(world.Whole, new RenderQueryOptions(), CancellationToken.None);

            features.Select(f => f.Id).Should().BeEquivalentTo(world.VisibleToA, "{0}", query.GetType().Name);
            var colors = features.ToDictionary(f => f.Id, f => f.Color, StringComparer.Ordinal);
            colors[world.OverrideId].Should().Be(RenderQueryWorld.OverrideColor);
            colors[world.RuleId].Should().Be(RenderQueryWorld.RuleColor);
            colors[world.RuledNoMatchId].Should().Be(RenderQueryWorld.RuledTypeColor);
            colors[world.DefaultLayerId].Should().Be(RenderQueryWorld.DefaultLayerColor);
            colors[world.PlainId].Should().Be(RenderQueryWorld.PlainTypeColor);
            colors[world.SharedId].Should().Be(RenderQueryWorld.PlainTypeColor);
        }
    }

    [Fact]
    public async Task GetRenderableAsync_WithAnAssetTypeFilter_PaintsTheSameFeaturesInTheSameColors()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var (pushdown, generic) = QueriesOf(world, fixture);

        foreach (var type in new[] { world.RuledType, world.DefaultLayerType, world.PlainType })
        {
            var options = new RenderQueryOptions { AssetTypeId = type.Id.ToString() };

            var fromPushdown = await pushdown.GetRenderableAsync(world.Whole, options, CancellationToken.None);
            var fromGeneric = await generic.GetRenderableAsync(world.Whole, options, CancellationToken.None);

            fromPushdown.Should().NotBeEmpty();
            fromPushdown.Should().OnlyContain(f => f.AssetTypeId == type.Id.ToString());
            Painted(fromPushdown).Should().Equal(Painted(fromGeneric), "for type {0}", type.Name);
        }
    }

    [Fact]
    public async Task GetRenderableAsync_ForEveryZoomAndSeveralCaps_ReturnsTheSameFeaturesInTheSameOrder()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var (pushdown, generic) = QueriesOf(world, fixture);

        foreach (var zoom in Enumerable.Range(0, 21).Cast<int?>().Append(null))
        {
            foreach (var maxCount in new[] { 1, 3, 100 })
            {
                var options = new RenderQueryOptions { Zoom = zoom, MaxCount = maxCount };

                var fromPushdown = await pushdown.GetRenderableAsync(world.Whole, options, CancellationToken.None);
                var fromGeneric = await generic.GetRenderableAsync(world.Whole, options, CancellationToken.None);

                fromPushdown.Select(f => (f.Id, f.Color)).Should().Equal(
                    fromGeneric.Select(f => (f.Id, f.Color)), "at zoom {0} with at most {1}", zoom, maxCount);
            }
        }
    }

    [Fact]
    public async Task Render_FeatureWithoutLayer_KeepsItsAssetTypeColor()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var (pushdown, generic) = QueriesOf(world, fixture);

        foreach (var query in new IAssetRenderQuery[] { pushdown, generic })
        {
            var features = await query.GetRenderableAsync(world.AroundPlain, new RenderQueryOptions(), CancellationToken.None);

            features.Should().ContainSingle().Which.Color.Should().Be(RenderQueryWorld.PlainTypeColor, "{0}", query.GetType().Name);
        }
    }

    [Fact]
    public async Task GetFeatureInfoAsync_ForSeveralBoxesAndCounts_ReturnsTheSameFeaturesInTheSameOrder()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var (pushdown, generic) = QueriesOf(world, fixture);

        foreach (var bbox in new[] { world.Whole, world.AroundPlain, world.Empty })
        {
            foreach (var maxCount in new[] { 1, 3, 100 })
            {
                var fromPushdown = await pushdown.GetFeatureInfoAsync(bbox, maxCount, CancellationToken.None);
                var fromGeneric = await generic.GetFeatureInfoAsync(bbox, maxCount, CancellationToken.None);

                fromPushdown.Should().Equal(fromGeneric, "for {0} and {1} features", bbox, maxCount);
            }
        }
    }

    [Fact]
    public async Task GetFeatureInfoAsync_OverTheWholeArea_IsOrderedByIdAndCarriesTheFeaturesProperties()
    {
        await using var world = await RenderQueryWorld.SeedAsync(fixture);
        var (pushdown, generic) = QueriesOf(world, fixture);

        foreach (var query in new IAssetRenderQuery[] { pushdown, generic })
        {
            var info = await query.GetFeatureInfoAsync(world.Whole, 100, CancellationToken.None);

            info.Select(i => i.Id).Should().Equal(world.VisibleToA.Order(StringComparer.Ordinal), "{0}", query.GetType().Name);
            info.Single(i => string.Equals(i.Id, world.PlainId, StringComparison.Ordinal)).Should().Be(
                new FeatureInfo(world.PlainId, $"name {world.PlainId}", world.PlainType.Id.ToString(), $"description {world.PlainId}"));
        }
    }
}
