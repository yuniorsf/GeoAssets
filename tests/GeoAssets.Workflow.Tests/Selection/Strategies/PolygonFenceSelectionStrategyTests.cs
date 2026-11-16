using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Selection.Strategies;
using Xunit;

namespace GeoAssets.Workflow.Tests.Selection.Strategies;

/// <summary>One or more tests per guard/branch id of <see cref="PolygonFenceSelectionStrategy"/> (XD01-307).</summary>
public class PolygonFenceSelectionStrategyTests
{
    private static readonly PolygonFenceSelectionStrategy Sut = new();

    // 10° × 10° square with its south-west corner on the origin.
    private static double[][][] Fence() => [[[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]]];

    private static Dictionary<string, object> Parameters(params (string Key, object Value)[] extra)
    {
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal) { ["rings"] = Fence() };
        foreach (var (key, value) in extra)
            parameters[key] = value;
        return parameters;
    }

    private static FeatureSelectionContext SpyContext(RecordingSpatialQuery spy, Dictionary<string, object> parameters, Guid? organizationId = null) =>
        // Repository is deliberately left unset: a strategy that reached for it would fail.
        new() { SpatialQuery = spy, Parameters = parameters, OrganizationId = organizationId };

    private static TestAssetProvider Repo(params GeoFeature[] features)
    {
        var repo = new TestAssetProvider();
        foreach (var feature in features)
            repo.Add(feature);
        return repo;
    }

    private static GeoFeature Point(string id, double lon, double lat, string assetTypeId = "pole") =>
        new() { Id = id, Geometry = new GeoPoint(lon, lat), Properties = new GeoFeatureProperties { AssetTypeId = assetTypeId } };

    [Fact]
    public void Metadata_MatchesTheExportedStrategyId()
    {
        Sut.StrategyId.Should().Be("polygon-fence");
        PolygonFenceSelectionStrategy.Id.Should().Be("polygon-fence");
        PolygonFenceSelectionStrategy.RingsParameter.Should().Be("rings");
        PolygonFenceSelectionStrategy.AssetTypeIdsParameter.Should().Be("assetTypeIds");
        PolygonFenceSelectionStrategy.MaxResultsParameter.Should().Be("maxResults");
    }

    // ── Guards ───────────────────────────────────────────────────────────────

    [Fact]
    public Task PFS_G1_NullContext_ThrowsArgumentNullException()
    {
        var act = () => Sut.SelectAsync(null!);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PFS_G2_RingsParameterMissing_ThrowsKeyNotFoundException()
    {
        var spy = new RecordingSpatialQuery();
        var context = SpyContext(spy, new Dictionary<string, object>(StringComparer.Ordinal));

        var act = () => Sut.SelectAsync(context);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        spy.Calls.Should().Be(0);
    }

    public static TheoryData<object> NotRings => new()
    {
        "not rings",
        42,
        new[] { 1.0, 2.0 },
        JsonSerializer.SerializeToElement("not rings"),
        JsonSerializer.SerializeToElement(new[] { "a", "b" }),
        JsonSerializer.SerializeToElement(new { type = "Polygon" }),
    };

    [Theory]
    [MemberData(nameof(NotRings))]
    public async Task PFS_G3_RingsNotConvertibleToCoordinateRings_ThrowsInvalidCastException(object rings)
    {
        var spy = new RecordingSpatialQuery();
        var context = SpyContext(spy, new Dictionary<string, object>(StringComparer.Ordinal) { ["rings"] = rings });

        var act = () => Sut.SelectAsync(context);

        await act.Should().ThrowAsync<InvalidCastException>();
        spy.Calls.Should().Be(0);
    }

    [Fact]
    public async Task PFS_G4_InvalidFence_PropagatesTheValidatorsExceptionUnchanged()
    {
        var spy = new RecordingSpatialQuery();
        double[][][] unclosed = [[[0, 0], [10, 0], [10, 10], [0, 10]]];
        var context = SpyContext(spy, new Dictionary<string, object>(StringComparer.Ordinal) { ["rings"] = unclosed });

        var act = () => Sut.SelectAsync(context);

        (await act.Should().ThrowExactlyAsync<InvalidSelectionFenceException>())
            .Which.Defect.Should().Be(SelectionFenceDefect.NotClosed);
        spy.Calls.Should().Be(0);
    }

    [Fact]
    public async Task PFS_G4_InvalidFenceAfterJsonReload_StillReportsTheTypedDefect()
    {
        var spy = new RecordingSpatialQuery();
        var bowTie = JsonSerializer.SerializeToElement(new double[][][] { [[0, 0], [1, 1], [1, 0], [0, 1], [0, 0]] });
        var context = SpyContext(spy, new Dictionary<string, object>(StringComparer.Ordinal) { ["rings"] = bowTie });

        var act = () => Sut.SelectAsync(context);

        (await act.Should().ThrowExactlyAsync<InvalidSelectionFenceException>())
            .Which.Defect.Should().Be(SelectionFenceDefect.SelfIntersecting);
    }

    // ── PFS-B1 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PFS_B1_AssetTypeIdsAbsent_ForwardsNoTypeFilter()
    {
        var spy = new RecordingSpatialQuery();

        await Sut.SelectAsync(SpyContext(spy, Parameters()));

        spy.Filter!.AssetTypeIds.Should().BeNull();
    }

    [Fact]
    public async Task PFS_B1_AssetTypeIdsEmpty_ForwardsNoTypeFilter()
    {
        var spy = new RecordingSpatialQuery();

        await Sut.SelectAsync(SpyContext(spy, Parameters(("assetTypeIds", Array.Empty<string>()))));

        spy.Filter!.AssetTypeIds.Should().BeNull();
    }

    [Fact]
    public async Task PFS_B1_AssetTypeIdsPresent_ForwardsTheFilter()
    {
        var spy = new RecordingSpatialQuery();

        await Sut.SelectAsync(SpyContext(spy, Parameters(("assetTypeIds", new List<string> { "pole", "duct" }))));

        spy.Filter!.AssetTypeIds.Should().Equal("pole", "duct");
    }

    [Fact]
    public async Task PFS_B1_AssetTypeIdsPresent_KeepsOnlyThoseTypes()
    {
        var repo = Repo(Point("pole-1", 1, 1), Point("box-1", 2, 2, "box"));
        var context = new FeatureSelectionContext
        {
            Repository = repo,
            Parameters = Parameters(("assetTypeIds", new[] { "box" })),
        };

        var result = await Sut.SelectAsync(context);

        result.Select(f => f.Id).Should().Equal("box-1");
    }

    // ── PFS-B2 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PFS_B2_MaxResultsAbsent_MeansUnbounded()
    {
        var spy = new RecordingSpatialQuery();

        await Sut.SelectAsync(SpyContext(spy, Parameters()));

        spy.Filter!.MaxResults.Should().Be(int.MaxValue);
    }

    [Fact]
    public async Task PFS_B2_MaxResultsPresent_IsForwarded()
    {
        var spy = new RecordingSpatialQuery();

        await Sut.SelectAsync(SpyContext(spy, Parameters(("maxResults", 25))));

        spy.Filter!.MaxResults.Should().Be(25);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PFS_B2_MaxResultsZeroOrLess_ThrowsArgumentOutOfRangeException(int maxResults)
    {
        var spy = new RecordingSpatialQuery();

        var act = () => Sut.SelectAsync(SpyContext(spy, Parameters(("maxResults", maxResults))));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        spy.Calls.Should().Be(0);
    }

    // ── PFS-B3 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PFS_B3_NoMatch_ReturnsEmptyListNotNull()
    {
        var context = new FeatureSelectionContext { Repository = Repo(Point("far", 50, 50)), Parameters = Parameters() };

        var result = await Sut.SelectAsync(context);

        result.Should().NotBeNull().And.BeEmpty();
    }

    // ── PFS-B4 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PFS_B4_ResultsAreOrderedByIdOrdinal_SoTruncationIsDeterministic()
    {
        // Insertion order differs from id order; "B" sorts before "a" only under ordinal comparison.
        var repo = Repo(Point("c", 1, 1), Point("a", 2, 2), Point("B", 3, 3), Point("b", 4, 4));

        var all = await Sut.SelectAsync(new FeatureSelectionContext { Repository = repo, Parameters = Parameters() });
        var cut = await Sut.SelectAsync(
            new FeatureSelectionContext { Repository = repo, Parameters = Parameters(("maxResults", 2)) });

        all.Select(f => f.Id).Should().Equal("B", "a", "b", "c");
        cut.Select(f => f.Id).Should().Equal("B", "a");
    }

    // ── PFS-B5 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PFS_B5_CancelledToken_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var context = new FeatureSelectionContext { Repository = Repo(Point("a", 1, 1)), Parameters = Parameters() };

        var act = () => Sut.SelectAsync(context, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PFS_B5_ForwardsTheCallersTokenToTheSpatialQuery()
    {
        using var cts = new CancellationTokenSource();
        var spy = new RecordingSpatialQuery();

        await Sut.SelectAsync(SpyContext(spy, Parameters()), cts.Token);

        spy.Token.Should().Be(cts.Token);
    }

    // ── Port usage ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Select_GoesThroughTheContextsSpatialQuery_WithTheValidatedFenceAndTheContextsOrganization()
    {
        var organizationId = Guid.NewGuid();
        var answer = Point("from-port", 1, 1);
        var spy = new RecordingSpatialQuery(answer);

        var result = await Sut.SelectAsync(SpyContext(spy, Parameters(), organizationId));

        spy.Calls.Should().Be(1);
        spy.Area!.Coordinates.Should().BeEquivalentTo(Fence(), o => o.WithStrictOrdering());
        spy.Filter!.OrganizationId.Should().Be(organizationId);
        result.Should().Equal(answer);
    }

    [Fact]
    public async Task Select_NoOrganizationOnTheContext_ForwardsNoOrganizationPredicate()
    {
        var spy = new RecordingSpatialQuery();

        await Sut.SelectAsync(SpyContext(spy, Parameters()));

        spy.Filter!.OrganizationId.Should().BeNull();
    }

    [Fact]
    public async Task Select_FeatureExactlyOnTheFence_IsSelected_WhereBoundingBoxWouldNotSelectIt()
    {
        var repo = Repo(Point("on-edge", 10, 5), Point("inside", 5, 5), Point("outside", 10.5, 5));

        var fence = await Sut.SelectAsync(new FeatureSelectionContext { Repository = repo, Parameters = Parameters() });
        var box = await new BoundingBoxSelectionStrategy().SelectAsync(new FeatureSelectionContext
        {
            Repository = repo,
            Parameters = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["minLon"] = 0.0, ["minLat"] = 0.0, ["maxLon"] = 10.0, ["maxLat"] = 10.0,
            },
        });

        fence.Select(f => f.Id).Should().Equal("inside", "on-edge");
        box.Select(f => f.Id).Should().Equal("inside");
    }
}
