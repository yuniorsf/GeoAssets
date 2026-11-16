using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Selection.Strategies;
using Xunit;

namespace GeoAssets.Workflow.Tests.Selection.Strategies;

/// <summary>One or more tests per guard/branch id of <see cref="IdentifierListSelectionStrategy"/> (XD01-307).</summary>
public class IdentifierListSelectionStrategyTests
{
    private static readonly IdentifierListSelectionStrategy Sut = new();

    private static Dictionary<string, object> Parameters(object? attributeKey = null, object? values = null, object? assetTypeIds = null)
    {
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);
        if (attributeKey is not null) parameters["attributeKey"] = attributeKey;
        if (values is not null) parameters["values"] = values;
        if (assetTypeIds is not null) parameters["assetTypeIds"] = assetTypeIds;
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

    private static GeoFeature Plated(string id, string plate, string assetTypeId = "pole") =>
        new()
        {
            Id = id,
            Properties = new GeoFeatureProperties
            {
                AssetTypeId = assetTypeId,
                CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["plate"] = plate },
            },
        };

    [Fact]
    public void Metadata_MatchesTheExportedStrategyId()
    {
        Sut.StrategyId.Should().Be("identifier-list");
        IdentifierListSelectionStrategy.Id.Should().Be("identifier-list");
        IdentifierListSelectionStrategy.AttributeKeyParameter.Should().Be("attributeKey");
        IdentifierListSelectionStrategy.ValuesParameter.Should().Be("values");
        IdentifierListSelectionStrategy.AssetTypeIdsParameter.Should().Be("assetTypeIds");
        IdentifierListSelectionStrategy.FeatureIdKey.Should().Be("id");
        IdentifierListSelectionStrategy.MaxDistinctValues.Should().Be(5000);
    }

    // ── Guards ───────────────────────────────────────────────────────────────

    [Fact]
    public Task IDL_G1_NullContext_ThrowsArgumentNullException()
    {
        var act = () => Sut.SelectAsync(null!);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task IDL_G2_AttributeKeyMissing_ThrowsKeyNotFoundException()
    {
        var spy = new RecordingSpatialQuery();

        var act = () => Sut.SelectAsync(SpyContext(spy, Parameters(values: new[] { "1" })));

        await act.Should().ThrowAsync<KeyNotFoundException>();
        spy.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task IDL_G2_AttributeKeyBlank_ThrowsArgumentException(string attributeKey)
    {
        var spy = new RecordingSpatialQuery();

        var act = () => Sut.SelectAsync(SpyContext(spy, Parameters(attributeKey, new[] { "1" })));

        await act.Should().ThrowExactlyAsync<ArgumentException>();
        spy.Calls.Should().Be(0);
    }

    [Fact]
    public async Task IDL_G3_ValuesMissing_ThrowsKeyNotFoundException()
    {
        var spy = new RecordingSpatialQuery();

        var act = () => Sut.SelectAsync(SpyContext(spy, Parameters("plate")));

        await act.Should().ThrowAsync<KeyNotFoundException>();
        spy.Calls.Should().Be(0);
    }

    public static TheoryData<object> NotAStringList => new()
    {
        42,
        new[] { 1, 2 },
        JsonSerializer.SerializeToElement(42),
        JsonSerializer.SerializeToElement(new[] { 1, 2 }),
        JsonSerializer.SerializeToElement(new { plate = "1" }),
    };

    [Theory]
    [MemberData(nameof(NotAStringList))]
    public async Task IDL_G3_ValuesNotAStringList_ThrowsInvalidCastException(object values)
    {
        var spy = new RecordingSpatialQuery();

        var act = () => Sut.SelectAsync(SpyContext(spy, Parameters("plate", values)));

        await act.Should().ThrowAsync<InvalidCastException>();
        spy.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData]
    [InlineData("", "   ", "\t")]
    public async Task IDL_G4_NothingLeftAfterNormalization_ThrowsArgumentException(params string[] values)
    {
        var spy = new RecordingSpatialQuery();

        var act = () => Sut.SelectAsync(SpyContext(spy, Parameters("plate", values)));

        await act.Should().ThrowExactlyAsync<ArgumentException>();
        spy.Calls.Should().Be(0);
    }

    [Fact]
    public async Task IDL_G5_MoreThanFiveThousandDistinctValues_ThrowsArgumentOutOfRangeException()
    {
        var spy = new RecordingSpatialQuery();
        var values = Enumerable.Range(0, 5001).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();

        var act = () => Sut.SelectAsync(SpyContext(spy, Parameters("plate", values)));

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        spy.Calls.Should().Be(0);
    }

    [Fact]
    public async Task IDL_G5_ExactlyFiveThousandDistinctValues_IsAccepted_AndDuplicatesDoNotCountTowardsTheCeiling()
    {
        var spy = new RecordingSpatialQuery();
        var distinct = Enumerable.Range(0, 5000).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToList();
        List<string> values = [.. distinct, .. distinct.Select(v => $" {v} ")];

        await Sut.SelectAsync(SpyContext(spy, Parameters("plate", values)));

        spy.Values.Should().HaveCount(5000);
    }

    // ── IDL-B1 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task IDL_B1_ValuesAreTrimmed_BlanksDropped_AndDeduplicatedOrdinally()
    {
        var spy = new RecordingSpatialQuery();
        string[] values = [" 100 ", "100", "", "  ", "abc", "ABC", "abc\t", null!];

        await Sut.SelectAsync(SpyContext(spy, Parameters("plate", values)));

        spy.Values.Should().Equal("100", "abc", "ABC");
        spy.AttributeKey.Should().Be("plate");
    }

    // ── IDL-B2 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task IDL_B2_ReservedIdKey_MatchesFeatureIdNotACustomAttribute()
    {
        var decoy = Plated("f-2", "x");
        decoy.Properties.CustomAttributes["id"] = "f-1";
        var repo = Repo(Plated("f-1", "x"), decoy, Plated("f-3", "x"));
        var context = new FeatureSelectionContext
        {
            Repository = repo,
            Parameters = Parameters("id", new[] { "f-1", "f-3", "missing" }),
        };

        var result = await Sut.SelectAsync(context);

        result.Select(f => f.Id).Should().Equal("f-1", "f-3");
    }

    // ── IDL-B3 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task IDL_B3_AssetTypeIdsAbsentOrEmpty_ForwardsNoTypeFilter()
    {
        var absent = new RecordingSpatialQuery();
        var empty = new RecordingSpatialQuery();

        await Sut.SelectAsync(SpyContext(absent, Parameters("plate", new[] { "1" })));
        await Sut.SelectAsync(SpyContext(empty, Parameters("plate", new[] { "1" }, Array.Empty<string>())));

        absent.Filter!.AssetTypeIds.Should().BeNull();
        empty.Filter!.AssetTypeIds.Should().BeNull();
    }

    [Fact]
    public async Task IDL_B3_AssetTypeIdsPresent_ForwardsTheFilter_AndKeepsOnlyThoseTypes()
    {
        var spy = new RecordingSpatialQuery();
        var repo = Repo(Plated("pole-1", "100"), Plated("box-1", "100", "box"));

        await Sut.SelectAsync(SpyContext(spy, Parameters("plate", new[] { "100" }, new[] { "box" })));
        var result = await Sut.SelectAsync(new FeatureSelectionContext
        {
            Repository = repo,
            Parameters = Parameters("plate", new[] { "100" }, new[] { "box" }),
        });

        spy.Filter!.AssetTypeIds.Should().Equal("box");
        result.Select(f => f.Id).Should().Equal("box-1");
    }

    // ── IDL-B4 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task IDL_B4_OneValueMatchingSeveralFeatures_ReturnsAllOfThem()
    {
        var repo = Repo(Plated("a", "100"), Plated("b", "100"), Plated("c", "200"), Plated("d", "300"));
        var context = new FeatureSelectionContext
        {
            Repository = repo,
            Parameters = Parameters("plate", new[] { "100", "200", "not-found" }),
        };

        var result = await Sut.SelectAsync(context);

        result.Select(f => f.Id).Should().Equal("a", "b", "c");
    }

    // ── IDL-B5 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task IDL_B5_ResultsAreOrderedByIdOrdinal()
    {
        var repo = Repo(Plated("c", "1"), Plated("a", "1"), Plated("B", "1"), Plated("b", "1"));
        var context = new FeatureSelectionContext { Repository = repo, Parameters = Parameters("plate", new[] { "1" }) };

        var result = await Sut.SelectAsync(context);

        result.Select(f => f.Id).Should().Equal("B", "a", "b", "c");
    }

    [Fact]
    public async Task IDL_B5_CancelledToken_ThrowsOperationCanceledException()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var context = new FeatureSelectionContext
        {
            Repository = Repo(Plated("a", "1")),
            Parameters = Parameters("plate", new[] { "1" }),
        };

        var act = () => Sut.SelectAsync(context, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task IDL_B5_ForwardsTheCallersTokenToTheSpatialQuery()
    {
        using var cts = new CancellationTokenSource();
        var spy = new RecordingSpatialQuery();

        await Sut.SelectAsync(SpyContext(spy, Parameters("plate", new[] { "1" })), cts.Token);

        spy.Token.Should().Be(cts.Token);
    }

    // ── Port usage ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Select_GoesThroughTheContextsSpatialQuery_UnboundedAndWithTheContextsOrganization()
    {
        var organizationId = Guid.NewGuid();
        var answer = Plated("from-port", "1");
        var spy = new RecordingSpatialQuery(answer);

        var result = await Sut.SelectAsync(SpyContext(spy, Parameters("plate", new[] { "1" }), organizationId));

        spy.Calls.Should().Be(1);
        spy.Filter!.OrganizationId.Should().Be(organizationId);
        spy.Filter.MaxResults.Should().Be(int.MaxValue);
        result.Should().Equal(answer);
    }

    [Fact]
    public async Task Select_NoMatch_ReturnsEmptyListNotNull()
    {
        var context = new FeatureSelectionContext
        {
            Repository = Repo(Plated("a", "1")),
            Parameters = Parameters("plate", new[] { "nope" }),
        };

        var result = await Sut.SelectAsync(context);

        result.Should().NotBeNull().And.BeEmpty();
    }
}
