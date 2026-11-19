using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.FieldService.Server.Tests;

/// <summary>
/// <see cref="GeoCoreAssetQueryAdapter"/> (XD01-386): the host's ports over the Core GeoAssets
/// Server's API — organization routing, the breadth-first descendants walk, paging, and failing
/// closed with <see cref="GeoCoreUnavailableException"/>.
/// </summary>
public sealed class GeoCoreAssetQueryAdapterTests
{
    private static readonly Guid Organization = Guid.Parse("0a1b2c3d-0000-0000-0000-00000000abcd");
    private static readonly GeoPoint AssetLocation = new(-46.633, -23.55);
    private const string EdgeKind = "feeds";

    private readonly FakeGeoCoreClient _core = new();
    private readonly FakeOrganizationScopedGeoCoreClient _organizationCore = new();
    private readonly FakeTimeProvider _time = new();

    private GeoCoreAssetQueryAdapter CreateSut(Guid? organizationId = null) =>
        new(_core, _organizationCore, new FixedTenantContext(organizationId ?? Guid.Empty), _time);

    private static GeoFeature Feature(string id, GeoGeometry? geometry = null, string assetTypeId = "pole") => new()
    {
        Id = id,
        Geometry = geometry,
        Properties = new GeoFeatureProperties { Name = id, AssetTypeId = assetTypeId },
    };

    private static string[] Ids(IEnumerable<GeoFeature> features) => [.. features.Select(f => f.Id)];

    // ── IsWithinDistanceAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task IsWithinDistanceAsync_PointWithinTolerance_ReturnsTrue()
    {
        _core.Features["asset-1"] = Feature("asset-1", AssetLocation);

        var result = await CreateSut().IsWithinDistanceAsync("asset-1", AssetLocation, 30, CancellationToken.None);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task IsWithinDistanceAsync_PointBeyondTolerance_ReturnsFalse()
    {
        _core.Features["asset-1"] = Feature("asset-1", AssetLocation);
        var farAway = new GeoPoint(AssetLocation.Longitude, AssetLocation.Latitude + 1);

        var result = await CreateSut().IsWithinDistanceAsync("asset-1", farAway, 30, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsWithinDistanceAsync_FeatureNotFound_ReturnsFalse()
    {
        var result = await CreateSut().IsWithinDistanceAsync("missing", AssetLocation, 30, CancellationToken.None);

        result.Should().BeFalse("a missing feature is 'not within distance', distinct from the core being unreachable");
    }

    [Fact]
    public async Task IsWithinDistanceAsync_FeatureWithoutGeometry_ReturnsFalse()
    {
        _core.Features["asset-1"] = Feature("asset-1", geometry: null);

        var result = await CreateSut().IsWithinDistanceAsync("asset-1", AssetLocation, 30, CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public Task IsWithinDistanceAsync_BlankFeatureId_Throws()
    {
        var act = () => CreateSut().IsWithinDistanceAsync("", AssetLocation, 30, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentException>();
    }

    // ── Organization routing ───────────────────────────────────────────────────

    [Fact]
    public async Task GetFeatureAsync_OrganizationSet_UsesOrganizationScopedClientOnly()
    {
        _organizationCore.Features["asset-1"] = Feature("asset-1");
        _core.Features["asset-1"] = Feature("asset-1");

        var feature = await CreateSut(Organization).GetFeatureAsync("asset-1", CancellationToken.None);

        feature.Should().BeSameAs(_organizationCore.Features["asset-1"]);
        _organizationCore.FeatureCalls.Should().ContainSingle().Which.Should().Be((Organization, "asset-1"));
        _core.TotalCalls.Should().Be(0, "the shared view must not be used when the request has an organization");
    }

    [Fact]
    public async Task GetFeatureAsync_OrganizationEmpty_UsesGeoCoreClientOnly()
    {
        _core.Features["asset-1"] = Feature("asset-1");

        var feature = await CreateSut().GetFeatureAsync("asset-1", CancellationToken.None);

        feature.Should().BeSameAs(_core.Features["asset-1"]);
        _core.GetFeatureCalls.Should().Be(1);
        _organizationCore.FeatureCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFeatureAsync_OrganizationSet_FeatureNotVisible_ReturnsNull()
    {
        var feature = await CreateSut(Organization).GetFeatureAsync("hidden", CancellationToken.None);

        feature.Should().BeNull();
    }

    [Fact]
    public async Task GetDescendantsAsync_OrganizationSet_UsesOrganizationScopedClientOnly()
    {
        _organizationCore.Descendants["sw1"] = [Feature("d1"), Feature("d2")];

        var result = await CreateSut(Organization).GetDescendantsAsync("sw1", EdgeKind, CancellationToken.None);

        Ids(result).Should().Equal("d1", "d2");
        _organizationCore.DescendantCalls.Should().ContainSingle().Which.Should().Be((Organization, "sw1", EdgeKind));
        _core.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task IsWithinDistanceAsync_OrganizationSet_ReadsThroughOrganizationScopedClient()
    {
        _organizationCore.Features["asset-1"] = Feature("asset-1", AssetLocation);

        var result = await CreateSut(Organization).IsWithinDistanceAsync("asset-1", AssetLocation, 30, CancellationToken.None);

        result.Should().BeTrue();
        _core.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetFeaturesByAssetTypeAsync_OrganizationSet_StillUsesSharedClient()
    {
        _core.Features["crew-1"] = Feature("crew-1", assetTypeId: "crew");

        var result = await CreateSut(Organization).GetFeaturesByAssetTypeAsync("crew", CancellationToken.None);

        Ids(result).Should().Equal("crew-1");
        _organizationCore.FeatureCalls.Should().BeEmpty();
        _organizationCore.DescendantCalls.Should().BeEmpty();
    }

    // ── Descendants walk (no organization) ─────────────────────────────────────

    [Fact]
    public async Task GetDescendantsAsync_Chain_ReturnsDownstreamFeaturesInWalkOrderExcludingStart()
    {
        AddNode("sw1", "a");
        AddNode("a", "b");
        AddNode("b");

        var result = await CreateSut().GetDescendantsAsync("sw1", EdgeKind, CancellationToken.None);

        Ids(result).Should().Equal("a", "b");
        _core.NeighborBatches.Select(b => b.ToArray()).Should().BeEquivalentTo(
            new[] { new[] { "sw1" }, ["a"], ["b"] }, o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task GetDescendantsAsync_DiamondGraph_VisitsSharedDescendantOnce()
    {
        AddNode("root", "a", "b");
        AddNode("a", "d");
        AddNode("b", "d");
        AddNode("d");

        var result = await CreateSut().GetDescendantsAsync("root", EdgeKind, CancellationToken.None);

        Ids(result).Should().Equal("a", "b", "d");
        _core.NeighborBatches.Should().HaveCount(3, "one batched request per level, and d is not expanded twice");
        _core.NeighborBatches[2].Should().Equal("d");
        _core.Queries.Single().FeatureIds.Should().Equal("a", "b", "d");
    }

    [Fact]
    public async Task GetDescendantsAsync_Cycle_Terminates()
    {
        AddNode("a", "b");
        AddNode("b", "c");
        AddNode("c", "a");

        var result = await CreateSut().GetDescendantsAsync("a", EdgeKind, CancellationToken.None);

        Ids(result).Should().Equal("b", "c", "a");
    }

    [Fact]
    public async Task GetDescendantsAsync_SelfLoop_Terminates()
    {
        AddNode("a", "a");

        var result = await CreateSut().GetDescendantsAsync("a", EdgeKind, CancellationToken.None);

        Ids(result).Should().Equal("a");
    }

    [Fact]
    public async Task GetDescendantsAsync_NoNeighbors_ReturnsEmptyWithoutQueryingFeatures()
    {
        var result = await CreateSut().GetDescendantsAsync("lonely", EdgeKind, CancellationToken.None);

        result.Should().BeEmpty();
        _core.Queries.Should().BeEmpty("there are no ids to fetch");
    }

    [Fact]
    public async Task GetDescendantsAsync_MoreThanTenThousandReachable_StopsAtTheCap()
    {
        var children = Enumerable.Range(0, 12_000).Select(i => $"c-{i:D5}").ToList();
        _core.Neighbors["root"] = children;
        _core.FeatureFactory = id => Feature(id);

        var result = await CreateSut().GetDescendantsAsync("root", EdgeKind, CancellationToken.None);

        result.Should().HaveCount(GeoCoreAssetQueryAdapter.MaxFeatures);
        Ids(result).Should().Equal(children.Take(GeoCoreAssetQueryAdapter.MaxFeatures));
        _core.Queries.Should().HaveCount(GeoCoreAssetQueryAdapter.MaxFeatures / GeoCoreAssetQueryAdapter.PageSize);
        _core.Queries.Should().OnlyContain(q => q.FeatureIds!.Count <= GeoCoreAssetQueryAdapter.PageSize);
    }

    [Fact]
    public async Task GetDescendantsAsync_LargeFrontier_RequestsNeighborsInPagesOfFiveHundredIds()
    {
        var children = Enumerable.Range(0, 1_200).Select(i => $"c-{i:D4}").ToList();
        _core.Neighbors["root"] = children;
        _core.FeatureFactory = id => Feature(id);
        var sut = CreateSut();

        await sut.GetDescendantsAsync("root", EdgeKind, CancellationToken.None);

        _core.NeighborBatches.Select(b => b.Count).Should().Equal(1, 500, 500, 200);
    }

    [Fact]
    public async Task GetDescendantsAsync_ReachedIdNotReturnedByCore_IsSkipped()
    {
        AddNode("root", "a", "ghost");
        _core.Features.Remove("ghost");

        var result = await CreateSut().GetDescendantsAsync("root", EdgeKind, CancellationToken.None);

        Ids(result).Should().Equal("a");
    }

    // ── Asset-type listing ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetFeaturesByAssetTypeAsync_PagesFiveHundredAtATimeUntilTotalCount()
    {
        for (var i = 0; i < 1_200; i++) _core.Features[$"crew-{i:D4}"] = Feature($"crew-{i:D4}", assetTypeId: "crew");
        _core.Features["pole-1"] = Feature("pole-1");

        var result = await CreateSut().GetFeaturesByAssetTypeAsync("crew", CancellationToken.None);

        result.Should().HaveCount(1_200);
        result.Should().OnlyContain(f => f.Properties.AssetTypeId == "crew");
        _core.Queries.Select(q => q.Skip).Should().Equal(0, 500, 1_000);
        _core.Queries.Should().OnlyContain(q => q.Take == 500 && q.AssetTypeId == "crew");
    }

    [Fact]
    public async Task GetFeaturesByAssetTypeAsync_ExactlyOnePage_StopsAfterOneRequest()
    {
        for (var i = 0; i < 3; i++) _core.Features[$"crew-{i}"] = Feature($"crew-{i}", assetTypeId: "crew");

        var result = await CreateSut().GetFeaturesByAssetTypeAsync("crew", CancellationToken.None);

        result.Should().HaveCount(3);
        _core.Queries.Should().ContainSingle();
    }

    [Fact]
    public async Task GetFeaturesByAssetTypeAsync_NoFeatures_ReturnsEmpty()
    {
        var result = await CreateSut().GetFeaturesByAssetTypeAsync("crew", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetFeaturesByAssetTypeAsync_MoreThanTenThousand_StopsAtTheCap()
    {
        for (var i = 0; i < 10_600; i++) _core.Features[$"crew-{i:D5}"] = Feature($"crew-{i:D5}", assetTypeId: "crew");

        var result = await CreateSut().GetFeaturesByAssetTypeAsync("crew", CancellationToken.None);

        result.Should().HaveCount(GeoCoreAssetQueryAdapter.MaxFeatures);
    }

    // ── Failing closed ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetFeatureAsync_HttpRequestException_ThrowsGeoCoreUnavailable()
    {
        var transport = new HttpRequestException("connection refused");
        _core.OnCall = _ => Task.FromException(transport);

        var act = () => CreateSut().GetFeatureAsync("asset-1", CancellationToken.None);

        (await act.Should().ThrowAsync<GeoCoreUnavailableException>()).Which.InnerException.Should().BeSameAs(transport);
    }

    [Fact]
    public Task IsWithinDistanceAsync_HttpRequestException_ThrowsGeoCoreUnavailableInsteadOfAnswering()
    {
        _core.OnCall = _ => Task.FromException(new HttpRequestException("503"));

        var act = () => CreateSut().IsWithinDistanceAsync("asset-1", AssetLocation, 30, CancellationToken.None);

        return act.Should().ThrowAsync<GeoCoreUnavailableException>();
    }

    [Fact]
    public Task GetDescendantsAsync_NeighborLookupHttpRequestException_ThrowsGeoCoreUnavailable()
    {
        _core.OnCall = _ => Task.FromException(new HttpRequestException("boom"));

        var act = () => CreateSut().GetDescendantsAsync("sw1", EdgeKind, CancellationToken.None);

        return act.Should().ThrowAsync<GeoCoreUnavailableException>();
    }

    [Fact]
    public Task GetDescendantsAsync_OrganizationSet_HttpRequestException_ThrowsGeoCoreUnavailable()
    {
        _organizationCore.OnCall = _ => Task.FromException(new HttpRequestException("boom"));

        var act = () => CreateSut(Organization).GetDescendantsAsync("sw1", EdgeKind, CancellationToken.None);

        return act.Should().ThrowAsync<GeoCoreUnavailableException>();
    }

    [Fact]
    public Task GetFeaturesByAssetTypeAsync_HttpRequestException_ThrowsGeoCoreUnavailable()
    {
        _core.OnCall = _ => Task.FromException(new HttpRequestException("boom"));

        var act = () => CreateSut().GetFeaturesByAssetTypeAsync("crew", CancellationToken.None);

        return act.Should().ThrowAsync<GeoCoreUnavailableException>();
    }

    [Fact]
    public async Task GetFeatureAsync_CoreNeverAnswers_ThrowsGeoCoreUnavailableOnceTheTimeoutElapses()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _core.OnCall = async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };

        var pending = CreateSut().GetFeatureAsync("asset-1", CancellationToken.None);
        await started.Task;
        pending.IsCompleted.Should().BeFalse("nothing has timed out yet");

        _time.Advance(GeoCoreAssetQueryAdapter.DefaultRequestTimeout + TimeSpan.FromSeconds(1));

        var act = () => pending;
        (await act.Should().ThrowAsync<GeoCoreUnavailableException>()).Which.Message.Should().Contain("10000 ms");
    }

    [Fact]
    public async Task GetFeatureAsync_AnswersBeforeTheTimeout_IsNotAffectedByLaterTimeAdvance()
    {
        _core.Features["asset-1"] = Feature("asset-1");
        var sut = CreateSut();

        var feature = await sut.GetFeatureAsync("asset-1", CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(5));

        feature.Should().NotBeNull();
    }

    [Fact]
    public async Task GetDescendantsAsync_OrganizationSet_CoreNeverAnswers_ThrowsGeoCoreUnavailableOnceTheTimeoutElapses()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _organizationCore.OnCall = async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };

        var pending = CreateSut(Organization).GetDescendantsAsync("sw1", EdgeKind, CancellationToken.None);
        await started.Task;
        _time.Advance(GeoCoreAssetQueryAdapter.DefaultRequestTimeout + TimeSpan.FromSeconds(1));

        var act = () => pending;
        await act.Should().ThrowAsync<GeoCoreUnavailableException>();
    }

    [Fact]
    public async Task GetFeatureAsync_CallerCancels_PropagatesOperationCanceledExceptionUnwrapped()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _core.OnCall = async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        };
        using var caller = new CancellationTokenSource();

        var pending = CreateSut().GetFeatureAsync("asset-1", caller.Token);
        await started.Task;
        await caller.CancelAsync();

        var act = () => pending;
        var thrown = await act.Should().ThrowAsync<OperationCanceledException>();
        thrown.Which.Should().NotBeOfType<GeoCoreUnavailableException>();
    }

    [Fact]
    public async Task GetDescendantsAsync_CallerAlreadyCancelled_PropagatesOperationCanceledException()
    {
        _core.OnCall = async token => await Task.Delay(Timeout.InfiniteTimeSpan, token);
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        var act = () => CreateSut().GetDescendantsAsync("sw1", EdgeKind, caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetFeaturesByAssetTypeAsync_CallerCancels_PropagatesOperationCanceledException()
    {
        _core.OnCall = async token => await Task.Delay(Timeout.InfiniteTimeSpan, token);
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        var act = () => CreateSut().GetFeaturesByAssetTypeAsync("crew", caller.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private void AddNode(string id, params string[] targets)
    {
        _core.Features[id] = Feature(id);
        _core.Neighbors[id] = [.. targets];
        foreach (var target in targets)
            _core.Features.TryAdd(target, Feature(target));
    }
}
