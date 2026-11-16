using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>Branches MAP-B6..B10 and MAP-B25 of the Uso Mútuo plan (XD01-310).</summary>
public class MapSelectionCoordinatorTests
{
    private const string DivId = "map1";

    private sealed class RecordingSelectionInterop : IMapSelectionInterop
    {
        public List<(string DivId, string[] Ids, MapFeatureState State)> StateCalls { get; } = [];
        public List<(string DivId, MapSelectionTool Tool)> ToolCalls { get; } = [];
        public Exception? FailStatesWith { get; set; }

        public Task SetFeatureStatesAsync(string divId, IReadOnlyCollection<string> featureIds, MapFeatureState state)
        {
            if (FailStatesWith is { } failure) return Task.FromException(failure);
            if (featureIds.Count > 0) StateCalls.Add((divId, [.. featureIds], state));
            return Task.CompletedTask;
        }

        public Task ClearFeatureStatesAsync(string divId) => Task.CompletedTask;

        public Task SetSelectionToolAsync(string divId, MapSelectionTool tool)
        {
            ToolCalls.Add((divId, tool));
            return Task.CompletedTask;
        }
    }

    /// <summary>Each call gets its own completion source, so a test decides when — and how — every
    /// resolve finishes.</summary>
    private sealed class ControllableResolver : IMapSelectionResolver
    {
        public List<(MapSelectionGesture Gesture, CancellationToken Token, TaskCompletionSource<IReadOnlyList<string>> Completion)> Calls { get; } = [];

        public Task<IReadOnlyList<string>> ResolveAsync(MapSelectionGesture gesture, CancellationToken ct)
        {
            var completion = new TaskCompletionSource<IReadOnlyList<string>>();
            Calls.Add((gesture, ct, completion));
            return completion.Task;
        }
    }

    private sealed class Harness : IDisposable
    {
        public MapSelectionSet Set { get; } = new(maxCount: 5000);
        public ControllableResolver Resolver { get; } = new();
        public RecordingSelectionInterop Interop { get; } = new();
        public MapSelectionCoordinator Sut { get; }
        public int StateChanges { get; private set; }

        public Harness()
        {
            Sut = new MapSelectionCoordinator(Set, Resolver, Interop, NullLogger<MapSelectionCoordinator>.Instance, DivId);
            Sut.StateChanged += () => StateChanges++;
        }

        public void Dispose() => Sut.Dispose();
    }

    private static MapSelectionGesture Gesture(MapSelectionTool tool = MapSelectionTool.Box, bool additive = false) =>
        new(tool, [[[-70, 18], [-69, 19]]], additive);

    // ── Tool ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(MapSelectionTool.ClickToggle)]
    [InlineData(MapSelectionTool.Box)]
    [InlineData(MapSelectionTool.Fence)]
    [InlineData(MapSelectionTool.None)]
    public async Task SetToolAsync_ArmsTheToolOnTheMap_AndExposesIt(MapSelectionTool tool)
    {
        using var h = new Harness();

        await h.Sut.SetToolAsync(tool);

        h.Interop.ToolCalls.Should().Equal((DivId, tool));
        h.Sut.Tool.Should().Be(tool);
        h.StateChanges.Should().Be(1);
    }

    // ── MAP-B6 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task MAP_B6_Click_WithClickToggleArmed_TogglesTheFeature_AndIsConsumed()
    {
        using var h = new Harness();
        await h.Sut.SetToolAsync(MapSelectionTool.ClickToggle);

        h.Sut.HandleClick("pole-1").Should().BeTrue();
        h.Set.Ids.Should().BeEquivalentTo("pole-1");

        h.Sut.HandleClick("pole-1").Should().BeTrue();
        h.Set.Ids.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MapSelectionTool.None)]
    [InlineData(MapSelectionTool.Box)]
    [InlineData(MapSelectionTool.Fence)]
    public async Task MAP_B6_Click_WithAnyOtherTool_IsNotConsumed_SoTheExistingFeatureClickRuns(MapSelectionTool tool)
    {
        using var h = new Harness();
        await h.Sut.SetToolAsync(tool);

        h.Sut.HandleClick("pole-1").Should().BeFalse();

        h.Set.Ids.Should().BeEmpty();
        h.Resolver.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task MAP_B6_Click_ThatOverflowsTheSelection_SurfacesAnOverflowNotice()
    {
        var set = new MapSelectionSet(maxCount: 1);
        using var sut = new MapSelectionCoordinator(
            set, new ControllableResolver(), new RecordingSelectionInterop(), NullLogger<MapSelectionCoordinator>.Instance, DivId);
        await sut.SetToolAsync(MapSelectionTool.ClickToggle);
        sut.HandleClick("a");

        sut.HandleClick("b").Should().BeTrue();

        set.Ids.Should().BeEquivalentTo("a");
        sut.Notice.Should().Be(new MapSelectionNotice(MapSelectionNoticeKind.Overflow, Count: 1, Limit: 1));
    }

    // ── MAP-B7 ───────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(MapSelectionTool.Box)]
    [InlineData(MapSelectionTool.Fence)]
    public async Task MAP_B7_Gesture_ResolvesThroughThePort_AndReplacesTheSelection(MapSelectionTool tool)
    {
        using var h = new Harness();
        h.Set.Add(["old-1", "keep"]);
        var gesture = Gesture(tool);

        var handling = h.Sut.HandleGestureAsync(gesture);
        h.Sut.IsResolving.Should().BeTrue();
        h.Resolver.Calls.Should().ContainSingle().Which.Gesture.Should().BeSameAs(gesture);
        h.Resolver.Calls[0].Completion.SetResult(["keep", "new-1"]);
        await handling;

        h.Set.Ids.Should().BeEquivalentTo("keep", "new-1");
        h.Sut.IsResolving.Should().BeFalse();
        h.Sut.Notice.Should().BeNull();
    }

    [Fact]
    public async Task MAP_B7_Gesture_WithTheAdditiveModifier_AddsToTheSelection()
    {
        using var h = new Harness();
        h.Set.Add(["old-1"]);

        var handling = h.Sut.HandleGestureAsync(Gesture(additive: true));
        h.Resolver.Calls[0].Completion.SetResult(["new-1", "old-1"]);
        await handling;

        h.Set.Ids.Should().BeEquivalentTo("old-1", "new-1");
    }

    [Fact]
    public async Task MAP_B7_Gesture_ThatMatchesNothing_EmptiesTheSelection_UnlessAdditive()
    {
        using var h = new Harness();
        h.Set.Add(["old-1"]);

        var additive = h.Sut.HandleGestureAsync(Gesture(additive: true));
        h.Resolver.Calls[0].Completion.SetResult([]);
        await additive;
        h.Set.Ids.Should().BeEquivalentTo("old-1");

        var replacing = h.Sut.HandleGestureAsync(Gesture());
        h.Resolver.Calls[1].Completion.SetResult([]);
        await replacing;
        h.Set.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task MAP_B7_Gesture_ResolvingMoreThanTheSelectionHolds_SurfacesAnOverflowNotice()
    {
        using var h = new Harness();

        var handling = h.Sut.HandleGestureAsync(Gesture());
        h.Resolver.Calls[0].Completion.SetResult([.. Enumerable.Range(0, 5003).Select(i => $"f{i}")]);
        await handling;

        h.Set.Ids.Should().HaveCount(5000);
        h.Sut.Notice.Should().Be(new MapSelectionNotice(MapSelectionNoticeKind.Overflow, Count: 3, Limit: 5000));
    }

    [Fact]
    public async Task HandleGestureAsync_Null_Throws()
    {
        using var h = new Harness();

        var act = () => h.Sut.HandleGestureAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── MAP-B8 ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task MAP_B8_NewerGesture_CancelsTheInFlightResolve()
    {
        using var h = new Harness();

        var first = h.Sut.HandleGestureAsync(Gesture());
        h.Resolver.Calls[0].Token.IsCancellationRequested.Should().BeFalse();

        var second = h.Sut.HandleGestureAsync(Gesture());

        h.Resolver.Calls.Should().HaveCount(2);
        h.Resolver.Calls[0].Token.IsCancellationRequested.Should().BeTrue();
        h.Resolver.Calls[1].Token.IsCancellationRequested.Should().BeFalse();

        h.Resolver.Calls[0].Completion.SetCanceled(h.Resolver.Calls[0].Token);
        h.Resolver.Calls[1].Completion.SetResult(["b"]);
        await Task.WhenAll(first, second);

        h.Set.Ids.Should().BeEquivalentTo("b");
        h.Sut.Notice.Should().BeNull();
    }

    [Fact]
    public async Task MAP_B8_LateResultOfACancelledResolve_IsDiscarded_EvenWhenItArrivesLast()
    {
        using var h = new Harness();

        var first = h.Sut.HandleGestureAsync(Gesture());
        var second = h.Sut.HandleGestureAsync(Gesture());
        h.Resolver.Calls[1].Completion.SetResult(["newer"]);
        await second;
        h.Sut.IsResolving.Should().BeFalse();

        // The superseded resolver ignores cancellation and answers anyway, after the newer one.
        h.Resolver.Calls[0].Completion.SetResult(["stale"]);
        await first;

        h.Set.Ids.Should().BeEquivalentTo("newer");
        h.Sut.IsResolving.Should().BeFalse();
    }

    [Fact]
    public async Task MAP_B8_LateFailureOfACancelledResolve_IsDiscarded_AndDoesNotClearTheBusyFlagOfTheNewerOne()
    {
        using var h = new Harness();

        var first = h.Sut.HandleGestureAsync(Gesture());
        var second = h.Sut.HandleGestureAsync(Gesture());

        h.Resolver.Calls[0].Completion.SetException(new HttpRequestException("stale failure"));
        await first;

        h.Sut.Notice.Should().BeNull();
        h.Sut.IsResolving.Should().BeTrue();

        h.Resolver.Calls[1].Completion.SetResult(["newer"]);
        await second;
        h.Sut.IsResolving.Should().BeFalse();
    }

    // ── MAP-B9 ───────────────────────────────────────────────────────────────

    public static TheoryData<Exception, MapSelectionNotice> ResolverFailures() => new()
    {
        { new SelectionTooLargeException(matchedAtLeast: 10_001, ceiling: 10_000), new MapSelectionNotice(MapSelectionNoticeKind.TooLarge, 10_001, 10_000) },
        { new InvalidSelectionFenceException(SelectionFenceDefect.SelfIntersecting), new MapSelectionNotice(MapSelectionNoticeKind.InvalidArea) },
        { new NotSupportedException("no resolver"), new MapSelectionNotice(MapSelectionNoticeKind.Unsupported) },
        { new HttpRequestException("503"), new MapSelectionNotice(MapSelectionNoticeKind.Failed) },
        { new KeyNotFoundException("unknown strategy"), new MapSelectionNotice(MapSelectionNoticeKind.Failed) },
        { new TaskCanceledException("the HTTP client timed out"), new MapSelectionNotice(MapSelectionNoticeKind.Failed) }
    };

    [Theory]
    [MemberData(nameof(ResolverFailures))]
    public async Task MAP_B9_ResolverFailure_LeavesTheSelectionUnchanged_AndSurfacesTheMatchingNotice(Exception failure, MapSelectionNotice expected)
    {
        using var h = new Harness();
        h.Set.Add(["a", "b"]);
        var events = new List<MapSelectionChange>();
        h.Set.Changed += events.Add;

        var handling = h.Sut.HandleGestureAsync(Gesture());
        h.Resolver.Calls[0].Completion.SetException(failure);
        await handling;

        h.Set.Ids.Should().BeEquivalentTo("a", "b");
        events.Should().BeEmpty();
        h.Sut.Notice.Should().Be(expected);
        h.Sut.Notice!.IsError.Should().BeTrue();
        h.Sut.IsResolving.Should().BeFalse();
    }

    [Fact]
    public async Task MAP_B9_ResolverThatThrowsSynchronously_IsHandledTheSameWay()
    {
        var set = new MapSelectionSet();
        set.Add(["a"]);
        using var sut = new MapSelectionCoordinator(
            set, new ThrowingResolver(), new RecordingSelectionInterop(), NullLogger<MapSelectionCoordinator>.Instance, DivId);

        await sut.HandleGestureAsync(Gesture());

        set.Ids.Should().BeEquivalentTo("a");
        sut.Notice.Should().Be(new MapSelectionNotice(MapSelectionNoticeKind.InvalidArea));
    }

    private sealed class ThrowingResolver : IMapSelectionResolver
    {
        public Task<IReadOnlyList<string>> ResolveAsync(MapSelectionGesture gesture, CancellationToken ct) =>
            throw new InvalidSelectionFenceException(SelectionFenceDefect.ZeroArea);
    }

    [Fact]
    public async Task MAP_B9_Notice_IsClearedByTheNextGesture_AndByDismissing()
    {
        using var h = new Harness();
        var failing = h.Sut.HandleGestureAsync(Gesture());
        h.Resolver.Calls[0].Completion.SetException(new HttpRequestException("503"));
        await failing;
        h.Sut.Notice.Should().NotBeNull();

        h.Sut.DismissNotice();
        h.Sut.Notice.Should().BeNull();

        var failingAgain = h.Sut.HandleGestureAsync(Gesture());
        h.Resolver.Calls[1].Completion.SetException(new HttpRequestException("503"));
        await failingAgain;
        var retry = h.Sut.HandleGestureAsync(Gesture());
        h.Sut.Notice.Should().BeNull();
        h.Resolver.Calls[2].Completion.SetResult([]);
        await retry;
    }

    // ── MAP-B10 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task MAP_B10_OnChanged_PushesAddedAsSelected_AndRemovedAsDefault()
    {
        using var h = new Harness();
        h.Set.Add(["a", "b"]);
        await h.Sut.PendingStatePushes;
        h.Interop.StateCalls.Clear();

        h.Set.Replace(["b", "c"]);
        await h.Sut.PendingStatePushes;

        h.Interop.StateCalls.Should().HaveCount(2);
        h.Interop.StateCalls.Should().ContainSingle(c => c.State == MapFeatureState.Selected)
            .Which.Should().BeEquivalentTo((DivId, new[] { "c" }, MapFeatureState.Selected));
        h.Interop.StateCalls.Should().ContainSingle(c => c.State == MapFeatureState.Default)
            .Which.Should().BeEquivalentTo((DivId, new[] { "a" }, MapFeatureState.Default));
    }

    [Fact]
    public async Task MAP_B10_OnChanged_FromAnyOwnerOfTheSet_IsPushed_AndClearRestoresDefault()
    {
        using var h = new Harness();

        h.Set.Add(["a"]);   // not through the coordinator — e.g. a list panel selecting rows
        h.Sut.Clear();
        await h.Sut.PendingStatePushes;

        h.Interop.StateCalls.Select(c => c.State).Should().Equal(MapFeatureState.Selected, MapFeatureState.Default);
        h.Interop.StateCalls.Should().OnlyContain(c => c.DivId == DivId && c.Ids.SequenceEqual(new[] { "a" }));
    }

    [Fact]
    public async Task MAP_B10_StatePushes_ReachTheMapInBatchesOfTwoThousandIds()
    {
        // The coordinator hands each change to IMapSelectionInterop whole; MapInteropService is
        // what cuts it into 2,000-id JS calls, so this drives the real one.
        var js = new RecordingJsRuntime();
        var interop = new MapInteropService(js, new TestAssetProvider(), Options.Create(new MapInteropOptions()));
        var set = new MapSelectionSet();
        using var sut = new MapSelectionCoordinator(set, new ControllableResolver(), interop, NullLogger<MapSelectionCoordinator>.Instance, DivId);

        set.Add([.. Enumerable.Range(0, 4500).Select(i => $"f{i}")]);
        await sut.PendingStatePushes;

        js.Calls.Should().HaveCount(3).And.OnlyContain(c => c.Identifier == "GeoAssets.setFeatureStates" && (string)c.Args[2]! == "selected");
        js.Calls.Select(c => ((string[])c.Args[1]!).Length).Should().Equal(2000, 2000, 500);
    }

    [Fact]
    public async Task MAP_B10_FailedPush_IsLogged_AndDoesNotBlockLaterPushesOrTheSelection()
    {
        using var h = new Harness();
        h.Interop.FailStatesWith = new JSException("map not ready");

        h.Set.Add(["a"]);
        await h.Sut.PendingStatePushes;
        h.Interop.FailStatesWith = null;
        h.Set.Add(["b"]);
        await h.Sut.PendingStatePushes;

        h.Set.Ids.Should().BeEquivalentTo("a", "b");
        h.Interop.StateCalls.Should().ContainSingle().Which.Ids.Should().Equal("b");
    }

    private sealed class RecordingJsRuntime : IJSRuntime
    {
        public List<(string Identifier, object?[] Args)> Calls { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            Calls.Add((identifier, args ?? []));
            return ValueTask.FromResult<TValue>(default!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    // ── MAP-B25 ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task MAP_B25_Dispose_UnsubscribesFromChanged_SoLaterChangesAreNotPushed()
    {
        var h = new Harness();
        h.Sut.Dispose();

        h.Set.Add(["a"]);
        await h.Sut.PendingStatePushes;

        h.Interop.StateCalls.Should().BeEmpty();
        h.StateChanges.Should().Be(0);
    }

    [Fact]
    public async Task MAP_B25_Dispose_CancelsThePendingResolve_AndItsLateResultIsDiscarded()
    {
        var h = new Harness();
        var handling = h.Sut.HandleGestureAsync(Gesture());

        h.Sut.Dispose();

        h.Resolver.Calls[0].Token.IsCancellationRequested.Should().BeTrue();
        h.Resolver.Calls[0].Completion.SetResult(["late"]);
        await handling;
        h.Set.Ids.Should().BeEmpty();
    }

    [Fact]
    public async Task MAP_B25_AfterDispose_ClicksGesturesAndToolChangesDoNothing_AndDisposeIsIdempotent()
    {
        var h = new Harness();
        await h.Sut.SetToolAsync(MapSelectionTool.ClickToggle);
        h.Sut.Dispose();
        h.Sut.Dispose();

        h.Sut.HandleClick("a").Should().BeFalse();
        await h.Sut.HandleGestureAsync(Gesture());
        await h.Sut.SetToolAsync(MapSelectionTool.Box);
        h.Sut.Clear();

        h.Set.Ids.Should().BeEmpty();
        h.Resolver.Calls.Should().BeEmpty();
        h.Interop.ToolCalls.Should().ContainSingle();
    }

    // ── Construction ─────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullOrBlankArguments_Throw()
    {
        var set = new MapSelectionSet();
        var resolver = new ControllableResolver();
        var interop = new RecordingSelectionInterop();
        var logger = NullLogger<MapSelectionCoordinator>.Instance;

        ((Action)(() => _ = new MapSelectionCoordinator(null!, resolver, interop, logger, DivId))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new MapSelectionCoordinator(set, null!, interop, logger, DivId))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new MapSelectionCoordinator(set, resolver, null!, logger, DivId))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new MapSelectionCoordinator(set, resolver, interop, null!, DivId))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new MapSelectionCoordinator(set, resolver, interop, logger, " "))).Should().Throw<ArgumentException>();
    }
}
