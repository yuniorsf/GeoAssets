using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using Xunit;

namespace GeoAssets.Core.Tests.Providers;

/// <summary>One or more tests per guard/branch id of <see cref="AssetProviderSpatialQuery"/> (XD01-307).</summary>
public class AssetProviderSpatialQueryTests
{
    private static readonly Guid OrgA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OrgB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly AssetSpatialFilter Unfiltered = new(null, null, int.MaxValue);

    // 10° × 10° square with its south-west corner on the origin.
    private static readonly GeoPolygon Area = new([(0, 0), (10, 0), (10, 10), (0, 10), (0, 0)]);

    private static GeoFeature Feature(
        string id, GeoGeometry? geometry = null, string assetTypeId = "pole", Guid? organizationId = null,
        params (string Key, string Value)[] attributes) =>
        new()
        {
            Id = id,
            Geometry = geometry,
            Properties = new GeoFeatureProperties
            {
                AssetTypeId = assetTypeId,
                OrganizationId = organizationId ?? Guid.Empty,
                CustomAttributes = attributes.ToDictionary(a => a.Key, a => a.Value, StringComparer.Ordinal),
            },
        };

    private static (AssetProviderSpatialQuery Sut, TestAssetProvider Provider) NewSut(params GeoFeature[] features)
    {
        var provider = new TestAssetProvider();
        foreach (var feature in features)
            provider.Add(feature);
        return (new AssetProviderSpatialQuery(provider), provider);
    }

    private static CancellationToken Cancelled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        return cts.Token;
    }

    // ── ASQ-G1 ───────────────────────────────────────────────────────────────

    [Fact]
    public void ASQ_G1_NullProvider_ThrowsArgumentNullException() =>
        FluentActions.Invoking(() => new AssetProviderSpatialQuery(null!))
            .Should().Throw<ArgumentNullException>().WithParameterName("provider");

    [Fact]
    public async Task ASQ_G1_FindCoveredBy_NullArea_ThrowsArgumentNullException()
    {
        var (sut, _) = NewSut();

        var act = () => sut.FindCoveredByAsync(null!, Unfiltered, CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("area");
    }

    [Fact]
    public async Task ASQ_G1_FindCoveredBy_NullFilter_ThrowsArgumentNullException()
    {
        var (sut, _) = NewSut();

        var act = () => sut.FindCoveredByAsync(Area, null!, CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("filter");
    }

    [Fact]
    public async Task ASQ_G1_FindByAttributeValues_NullValues_ThrowsArgumentNullException()
    {
        var (sut, _) = NewSut();

        var act = () => sut.FindByAttributeValuesAsync("plate", null!, Unfiltered, CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("values");
    }

    [Fact]
    public async Task ASQ_G1_FindByAttributeValues_NullFilter_ThrowsArgumentNullException()
    {
        var (sut, _) = NewSut();

        var act = () => sut.FindByAttributeValuesAsync("plate", ["1"], null!, CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("filter");
    }

    [Fact]
    public async Task ASQ_G1_FindByAttributeValues_NullAttributeKey_ThrowsArgumentNullException()
    {
        var (sut, _) = NewSut();

        var act = () => sut.FindByAttributeValuesAsync(null!, ["1"], Unfiltered, CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("attributeKey");
    }

    // ── ASQ-G2 ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public Task ASQ_G2_FindCoveredBy_NonPositiveMaxResults_ThrowsArgumentOutOfRangeException(int maxResults)
    {
        var (sut, _) = NewSut();

        var act = () => sut.FindCoveredByAsync(Area, new AssetSpatialFilter(null, null, maxResults), CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public Task ASQ_G2_FindByAttributeValues_NonPositiveMaxResults_ThrowsArgumentOutOfRangeException(int maxResults)
    {
        var (sut, _) = NewSut();

        var act = () => sut.FindByAttributeValuesAsync(
            "plate", ["1"], new AssetSpatialFilter(null, null, maxResults), CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // ── ASQ-G3 ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ASQ_G3_BlankAttributeKey_ThrowsArgumentException(string attributeKey)
    {
        var (sut, _) = NewSut();

        var act = () => sut.FindByAttributeValuesAsync(attributeKey, ["1"], Unfiltered, CancellationToken.None);

        (await act.Should().ThrowAsync<ArgumentException>()).WithParameterName(nameof(attributeKey));
    }

    // ── ASQ-B1 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASQ_B1_FeatureWithNullGeometry_IsExcluded()
    {
        var (sut, _) = NewSut(Feature("no-geometry"), Feature("inside", new GeoPoint(5, 5)));

        var result = await sut.FindCoveredByAsync(Area, Unfiltered, CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("inside");
    }

    // ── ASQ-B2 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASQ_B2_PointsExactlyOnTheEdgeAndOnAVertex_AreIncluded()
    {
        var (sut, provider) = NewSut(
            Feature("on-edge", new GeoPoint(10, 5)),
            Feature("on-vertex", new GeoPoint(0, 0)),
            Feature("inside", new GeoPoint(5, 5)),
            Feature("outside", new GeoPoint(10.0001, 5)));

        var result = await sut.FindCoveredByAsync(Area, Unfiltered, CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("inside", "on-edge", "on-vertex");
        // The strict "within" of IAssetProvider.GetWithin is exactly what this port does not use.
        provider.GetWithin(Area).Select(f => f.Id).Should().Equal("inside");
    }

    [Fact]
    public async Task ASQ_B2_LinePartlyOutsideTheArea_IsExcluded_LineOnTheBoundary_IsIncluded()
    {
        var (sut, _) = NewSut(
            Feature("crossing", new GeoLineString([(5, 5), (15, 5)])),
            Feature("along-edge", new GeoLineString([(0, 0), (10, 0)])));

        var result = await sut.FindCoveredByAsync(Area, Unfiltered, CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("along-edge");
    }

    // ── ASQ-B3 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASQ_B3_FindCoveredBy_OrganizationId_KeepsOnlyThatOrganizationsFeatures()
    {
        var (sut, _) = NewSut(
            Feature("a", new GeoPoint(1, 1), organizationId: OrgA),
            Feature("b", new GeoPoint(2, 2), organizationId: OrgB),
            Feature("none", new GeoPoint(3, 3)));

        var forA = await sut.FindCoveredByAsync(Area, new AssetSpatialFilter(null, OrgA, int.MaxValue), CancellationToken.None);
        var forAll = await sut.FindCoveredByAsync(Area, Unfiltered, CancellationToken.None);

        forA.Select(f => f.Id).Should().Equal("a");
        forAll.Select(f => f.Id).Should().Equal("a", "b", "none");
    }

    [Fact]
    public async Task ASQ_B3_FindByAttributeValues_OrganizationId_KeepsOnlyThatOrganizationsFeatures()
    {
        var (sut, _) = NewSut(
            Feature("a", organizationId: OrgA, attributes: ("plate", "100")),
            Feature("b", organizationId: OrgB, attributes: ("plate", "100")));

        var result = await sut.FindByAttributeValuesAsync(
            "plate", ["100"], new AssetSpatialFilter(null, OrgB, int.MaxValue), CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("b");
    }

    // ── ASQ-B4 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASQ_B4_NonEmptyAssetTypeIds_Filters()
    {
        var (sut, _) = NewSut(
            Feature("pole", new GeoPoint(1, 1), "pole"),
            Feature("box", new GeoPoint(2, 2), "box"),
            Feature("duct", new GeoPoint(3, 3), "duct"));

        var result = await sut.FindCoveredByAsync(
            Area, new AssetSpatialFilter(["pole", "duct"], null, int.MaxValue), CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("duct", "pole");
    }

    [Fact]
    public async Task ASQ_B4_NullOrEmptyAssetTypeIds_DoesNotFilter()
    {
        var (sut, _) = NewSut(Feature("pole", new GeoPoint(1, 1), "pole"), Feature("box", new GeoPoint(2, 2), "box"));

        var withNull = await sut.FindCoveredByAsync(Area, new AssetSpatialFilter(null, null, int.MaxValue), CancellationToken.None);
        var withEmpty = await sut.FindCoveredByAsync(Area, new AssetSpatialFilter([], null, int.MaxValue), CancellationToken.None);

        withNull.Select(f => f.Id).Should().Equal("box", "pole");
        withEmpty.Select(f => f.Id).Should().Equal("box", "pole");
    }

    [Fact]
    public async Task ASQ_B4_FindByAttributeValues_AssetTypeIds_Filters()
    {
        var (sut, _) = NewSut(
            Feature("pole", assetTypeId: "pole", attributes: ("plate", "100")),
            Feature("box", assetTypeId: "box", attributes: ("plate", "100")));

        var result = await sut.FindByAttributeValuesAsync(
            "plate", ["100"], new AssetSpatialFilter(["box"], null, int.MaxValue), CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("box");
    }

    // ── ASQ-B5 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASQ_B5_ResultIsCutAtMaxResults_AfterOrderingByIdOrdinal()
    {
        // Insertion order deliberately differs from id order; "B" < "a" only under ordinal comparison.
        var (sut, _) = NewSut(
            Feature("c", new GeoPoint(1, 1)),
            Feature("a", new GeoPoint(2, 2)),
            Feature("B", new GeoPoint(3, 3)),
            Feature("b", new GeoPoint(4, 4)));

        var all = await sut.FindCoveredByAsync(Area, Unfiltered, CancellationToken.None);
        var cut = await sut.FindCoveredByAsync(Area, new AssetSpatialFilter(null, null, 2), CancellationToken.None);

        all.Select(f => f.Id).Should().Equal("B", "a", "b", "c");
        cut.Select(f => f.Id).Should().Equal("B", "a");
    }

    [Fact]
    public async Task ASQ_B5_FindByAttributeValues_ResultIsCutAtMaxResults_AfterOrderingById()
    {
        var (sut, _) = NewSut(
            Feature("z", attributes: ("plate", "1")),
            Feature("y", attributes: ("plate", "2")),
            Feature("x", attributes: ("plate", "3")));

        var result = await sut.FindByAttributeValuesAsync(
            "plate", ["1", "2", "3"], new AssetSpatialFilter(null, null, 2), CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("x", "y");
    }

    // ── ASQ-B6 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASQ_B6_EmptyValues_ReturnsEmptyList()
    {
        var (sut, _) = NewSut(Feature("a", attributes: ("plate", "1")));

        var result = await sut.FindByAttributeValuesAsync("plate", [], Unfiltered, CancellationToken.None);

        result.Should().NotBeNull().And.BeEmpty();
    }

    // ── ASQ-B7 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ASQ_B7_FindCoveredBy_CancelledToken_ThrowsBeforeTouchingTheProvider()
    {
        var provider = new GetAllOnlyAssetProvider();
        var sut = new AssetProviderSpatialQuery(provider);

        var act = () => sut.FindCoveredByAsync(Area, Unfiltered, Cancelled());

        await act.Should().ThrowAsync<OperationCanceledException>();
        provider.GetAllCalls.Should().Be(0);
    }

    [Fact]
    public async Task ASQ_B7_FindByAttributeValues_CancelledToken_ThrowsBeforeTouchingTheProvider()
    {
        var provider = new GetAllOnlyAssetProvider();
        var sut = new AssetProviderSpatialQuery(provider);

        var act = () => sut.FindByAttributeValuesAsync("plate", ["1"], Unfiltered, Cancelled());

        await act.Should().ThrowAsync<OperationCanceledException>();
        provider.GetAllCalls.Should().Be(0);
    }

    [Fact]
    public Task ASQ_B7_FindByAttributeValues_CancelledTokenWithEmptyValues_StillThrows()
    {
        var (sut, _) = NewSut();

        var act = () => sut.FindByAttributeValuesAsync("plate", [], Unfiltered, Cancelled());

        return act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ASQ_B7_CancellationDuringTheScan_ThrowsInsteadOfReturningAResult()
    {
        using var cts = new CancellationTokenSource();
        var provider = new GetAllOnlyAssetProvider(cts.Cancel, Feature("a", new GeoPoint(1, 1)));
        var sut = new AssetProviderSpatialQuery(provider);

        var act = () => sut.FindCoveredByAsync(Area, Unfiltered, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        provider.GetAllCalls.Should().Be(1);
    }

    // ── Attribute lookup semantics shared with the strategies ────────────────

    [Fact]
    public async Task FindByAttributeValues_ReservedIdKey_MatchesFeatureIdNotACustomAttribute()
    {
        var (sut, _) = NewSut(
            Feature("f-1"),
            Feature("f-2", attributes: ("id", "f-1")),
            Feature("f-3"));

        var result = await sut.FindByAttributeValuesAsync(
            AssetSpatialQueryKeys.FeatureId, ["f-1", "f-3", "missing"], Unfiltered, CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("f-1", "f-3");
    }

    [Fact]
    public async Task FindByAttributeValues_MatchesExactOrdinalValues_AndReturnsEveryFeatureSharingAValue()
    {
        var (sut, _) = NewSut(
            Feature("a", attributes: ("plate", "100")),
            Feature("b", attributes: ("plate", "100")),
            Feature("c", attributes: ("plate", "ABC")),
            Feature("d", attributes: ("plate", "abc")),
            Feature("e", attributes: ("other", "100")),
            Feature("f"));

        var result = await sut.FindByAttributeValuesAsync("plate", ["100", "abc"], Unfiltered, CancellationToken.None);

        result.Select(f => f.Id).Should().Equal("a", "b", "d");
    }

    [Fact]
    public async Task FindCoveredBy_NoMatch_ReturnsEmptyListNotNull()
    {
        var (sut, _) = NewSut(Feature("far", new GeoPoint(50, 50)));

        var result = await sut.FindCoveredByAsync(Area, Unfiltered, CancellationToken.None);

        result.Should().NotBeNull().And.BeEmpty();
    }
}
