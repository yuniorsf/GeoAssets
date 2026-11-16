using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Selection.Strategies;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Selection;

/// <summary>
/// <see cref="BoundedFeatureSelector"/> on its own — the part of selection that is not about an
/// order: scope overrides, the ceiling, and what it reports as the parameters that actually ran.
/// </summary>
public sealed class BoundedFeatureSelectorTests : IDisposable
{
    private static readonly Guid Org = new("c0c0c0c0-0000-4000-8000-000000000308");
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _clock = new(Now);
    private readonly FeatureSelectionRegistry _registry;

    public BoundedFeatureSelectorTests() =>
        _registry = new FeatureSelectionRegistry(_clock, "no-such-plugins-directory", typeof(FeatureSelectionRegistry).Assembly);

    public void Dispose() => _registry.Dispose();

    private BoundedFeatureSelector Sut(RecordingSpatialQuery query) => new(_registry, query, assets: null, _clock);

    private static GeoFeature Feature(string id, string assetTypeId = "pole", Guid? organizationId = null) => new()
    {
        Id = id,
        Properties = new GeoFeatureProperties { AssetTypeId = assetTypeId, OrganizationId = organizationId ?? Org },
    };

    private static Dictionary<string, object> Fence() => new(StringComparer.Ordinal)
    {
        ["rings"] = new double[][][] { [[0, 0], [1, 0], [1, 1], [0, 1], [0, 0]] },
    };

    private static BoundedFeatureSelectionRequest Request(
        Dictionary<string, object>? parameters = null, int ceiling = 100, string strategyId = PolygonFenceSelectionStrategy.Id) =>
        new(strategyId, parameters ?? Fence(), Org, ceiling);

    [Fact]
    public void Constructor_NullRegistryOrSpatialQuery_ThrowsArgumentNullException()
    {
        FluentActions.Invoking(() => new BoundedFeatureSelector(null!, new RecordingSpatialQuery())).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new BoundedFeatureSelector(_registry, null!)).Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("polygon-fence", true)]
    [InlineData("POLYGON-FENCE", true)]
    [InlineData("identifier-list", true)]
    [InlineData("no-such-strategy", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasStrategy_MatchesRegisteredIdsCaseInsensitively(string? strategyId, bool expected) =>
        Sut(new RecordingSpatialQuery()).HasStrategy(strategyId!).Should().Be(expected);

    [Fact]
    public async Task SelectAsync_InvalidRequest_ThrowsBeforeAnyStrategyRuns()
    {
        var query = new RecordingSpatialQuery();
        var sut = Sut(query);

        await FluentActions.Invoking(() => sut.SelectAsync(null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.SelectAsync(Request(strategyId: " "), CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => sut.SelectAsync(Request(ceiling: 0), CancellationToken.None)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Invoking(() => sut.SelectAsync(Request(strategyId: "no-such-strategy"), CancellationToken.None))
            .Should().ThrowAsync<KeyNotFoundException>();
        query.Calls.Should().Be(0);
    }

    [Fact]
    public async Task SelectAsync_ReturnsDistinctMatchesOrderedById_AndTheSpecOfWhatActuallyRan()
    {
        var query = new RecordingSpatialQuery(Feature("p-3"), Feature("p-1"), Feature("p-3"), Feature("p-2"));

        var selection = await Sut(query).SelectAsync(Request(ceiling: 10), CancellationToken.None);

        selection.Features.Select(f => f.Id).Should().Equal("p-1", "p-2", "p-3");
        selection.Spec.StrategyId.Should().Be(PolygonFenceSelectionStrategy.Id);
        selection.Spec.ExecutedAt.Should().Be(Now.UtcDateTime);
        selection.Spec.Parameters[BoundedFeatureSelector.MaxResultsParameter].Should().Be(11);
        selection.Spec.Parameters.Should().ContainKey("rings");
        query.Filter!.OrganizationId.Should().Be(Org);
    }

    [Fact]
    public async Task SelectAsync_WithoutAllowedAssetTypes_LeavesTheCallersTypeFilterAsItIs()
    {
        var query = new RecordingSpatialQuery(Feature("p-1"), Feature("b-1", "box"));
        var typed = Fence();
        typed[BoundedFeatureSelector.AssetTypeIdsParameter] = new[] { "box" };

        var unfiltered = await Sut(query).SelectAsync(Request(), CancellationToken.None);
        var unfilteredTypes = query.Filter!.AssetTypeIds;
        var filtered = await Sut(query).SelectAsync(Request(typed), CancellationToken.None);

        unfilteredTypes.Should().BeNull();
        unfiltered.Features.Should().HaveCount(2);
        query.Filter!.AssetTypeIds.Should().Equal("box");
        filtered.Features.Select(f => f.Id).Should().Equal("b-1");
    }

    [Fact]
    public async Task SelectAsync_EmptyIntersectionOfAssetTypes_RunsNothing_ButStillStampsASpecFromTheClock()
    {
        var query = new RecordingSpatialQuery(Feature("p-1"));
        var parameters = Fence();
        parameters[BoundedFeatureSelector.AssetTypeIdsParameter] = new[] { "box" };

        var selection = await Sut(query).SelectAsync(
            Request(parameters) with { AllowedAssetTypeIds = ["pole"] }, CancellationToken.None);

        selection.Features.Should().BeEmpty();
        selection.Spec.ExecutedAt.Should().Be(Now.UtcDateTime);
        query.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public Task SelectAsync_NonPositiveMaxResultsFromTheCaller_ThrowsArgumentOutOfRangeException(int maxResults)
    {
        var parameters = Fence();
        parameters[BoundedFeatureSelector.MaxResultsParameter] = maxResults;

        var act = () => Sut(new RecordingSpatialQuery()).SelectAsync(Request(parameters), CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public Task SelectAsync_MaxResultsThatIsNotAnInteger_ThrowsInvalidCastException()
    {
        var parameters = Fence();
        parameters[BoundedFeatureSelector.MaxResultsParameter] = JsonSerializer.SerializeToElement("many");

        var act = () => Sut(new RecordingSpatialQuery()).SelectAsync(Request(parameters), CancellationToken.None);

        return act.Should().ThrowAsync<InvalidCastException>();
    }

    [Fact]
    public async Task SelectAsync_MoreMatchesThanTheCeiling_ThrowsSelectionTooLargeException()
    {
        var query = new RecordingSpatialQuery(Feature("p-1"), Feature("p-2"), Feature("p-3"));

        var act = () => Sut(query).SelectAsync(Request(ceiling: 2), CancellationToken.None);

        (await act.Should().ThrowAsync<SelectionTooLargeException>())
            .Which.Should().BeEquivalentTo(new { Ceiling = 2, MatchedAtLeast = 3 });
        query.Filter!.MaxResults.Should().Be(3);
    }

    [Fact]
    public async Task SelectAsync_IdentifierList_AJsonNullAttributeKeyCountsAsNotSupplied()
    {
        var query = new RecordingSpatialQuery(Feature("p-1"));
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["attributeKey"] = JsonSerializer.SerializeToElement<string?>(null),
            ["values"] = new[] { "P-1" },
        };

        await Sut(query).SelectAsync(
            Request(parameters, strategyId: IdentifierListSelectionStrategy.Id) with { DefaultAttributeKey = "plate" },
            CancellationToken.None);

        query.AttributeKey.Should().Be("plate");
    }
}
