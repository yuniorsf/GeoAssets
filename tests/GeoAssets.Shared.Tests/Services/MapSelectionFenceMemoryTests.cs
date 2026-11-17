using FluentAssertions;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>
/// <see cref="MapSelectionFenceMemory"/> and the coordinator's use of it (XD01-312): the fence is
/// remembered exactly while the selection is still that fence's result.
/// </summary>
public sealed class MapSelectionFenceMemoryTests
{
    private const string DivId = "map1";

    private static readonly double[][][] _ring = [[[-70, 18], [-69, 18], [-69, 19], [-70, 18]]];

    private sealed class SilentInterop : IMapSelectionInterop
    {
        public Task SetFeatureStatesAsync(string divId, IReadOnlyCollection<string> featureIds, MapFeatureState state) => Task.CompletedTask;
        public Task ClearFeatureStatesAsync(string divId) => Task.CompletedTask;
        public Task SetSelectionToolAsync(string divId, MapSelectionTool tool) => Task.CompletedTask;
        public Task ShowSelectionOutlineAsync(string divId, double[][][] rings) => Task.CompletedTask;
        public Task ClearSelectionOutlineAsync(string divId) => Task.CompletedTask;
    }

    /// <summary>Each call gets its own completion source, so a test decides when every resolve finishes.</summary>
    private sealed class ControllableResolver : IMapSelectionResolver
    {
        public List<TaskCompletionSource<IReadOnlyList<string>>> Calls { get; } = [];

        public Task<IReadOnlyList<string>> ResolveAsync(MapSelectionGesture gesture, CancellationToken ct)
        {
            var completion = new TaskCompletionSource<IReadOnlyList<string>>();
            Calls.Add(completion);
            return completion.Task;
        }
    }

    private sealed class Harness : IDisposable
    {
        public Harness(int maxCount = 100)
        {
            Set = new MapSelectionSet(maxCount);
            Memory = new MapSelectionFenceMemory(Set);
            Sut = new MapSelectionCoordinator(Set, Resolver, new SilentInterop(), NullLogger<MapSelectionCoordinator>.Instance, DivId, Memory);
        }

        public MapSelectionSet Set { get; }
        public MapSelectionFenceMemory Memory { get; }
        public ControllableResolver Resolver { get; } = new();
        public MapSelectionCoordinator Sut { get; }

        public Task GestureAsync(MapSelectionGesture gesture, params string[] ids)
        {
            var handled = Sut.HandleGestureAsync(gesture);
            Resolver.Calls[^1].SetResult(ids);
            return handled;
        }

        public void Dispose()
        {
            Sut.Dispose();
            Memory.Dispose();
        }
    }

    private static MapSelectionGesture Box(bool additive = false) => new(MapSelectionTool.Box, [[[-70, 18], [-69, 19]]], additive);

    private static MapSelectionGesture Fence(bool additive = false) => new(MapSelectionTool.Fence, [[[-70, 18], [-69, 18], [-69, 19]]], additive);

    // ── The memory itself ────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullSelection_Throws() =>
        FluentActions.Invoking(() => new MapSelectionFenceMemory(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void Remember_Null_Throws()
    {
        using var memory = new MapSelectionFenceMemory(new MapSelectionSet());
        FluentActions.Invoking(() => memory.Remember(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void StartsEmpty_RemembersRings_AndForgetsThemOnAnyChangeOfTheSelection()
    {
        var set = new MapSelectionSet();
        using var memory = new MapSelectionFenceMemory(set);
        memory.Rings.Should().BeNull();

        memory.Remember(_ring);
        memory.Rings.Should().BeSameAs(_ring);

        set.Add(["a"]);
        memory.Rings.Should().BeNull();
    }

    [Fact]
    public void AChangeThatChangesNothing_RaisesNoEvent_SoTheFenceIsKept()
    {
        var set = new MapSelectionSet();
        set.Add(["a"]);
        using var memory = new MapSelectionFenceMemory(set);
        memory.Remember(_ring);

        set.Add(["a"]);

        memory.Rings.Should().BeSameAs(_ring);
    }

    [Fact]
    public void Dispose_Unsubscribes_ForgetsTheFence_AndIgnoresLaterRemembers()
    {
        var set = new MapSelectionSet();
        var memory = new MapSelectionFenceMemory(set);
        memory.Remember(_ring);

        memory.Dispose();
        memory.Dispose();
        memory.Remember(_ring);
        set.Add(["a"]);

        memory.Rings.Should().BeNull();
    }

    // ── Recorded by the coordinator ──────────────────────────────────────────

    [Fact]
    public async Task ReplacingBox_IsRememberedAsItsClosedRectangle()
    {
        using var h = new Harness();

        await h.GestureAsync(Box(), "a", "b");

        h.Set.Ids.Should().BeEquivalentTo("a", "b");
        h.Memory.Rings.Should().BeEquivalentTo(
            new[] { new[] { new double[] { -70, 18 }, [-69, 18], [-69, 19], [-70, 19], [-70, 18] } },
            o => o.WithStrictOrdering());
    }

    [Fact]
    public async Task ReplacingFence_IsRememberedClosed()
    {
        using var h = new Harness();

        await h.GestureAsync(Fence(), "a");

        h.Memory.Rings.Should().ContainSingle().Which.Should().HaveCount(4);
        h.Memory.Rings![0][^1].Should().Equal(-70, 18);
    }

    [Fact]
    public async Task ReplacingGesture_ThatMatchesTheSelectionItAlreadyHas_IsStillRemembered()
    {
        using var h = new Harness();
        h.Set.Add(["a"]);

        await h.GestureAsync(Box(), "a");

        h.Memory.Rings.Should().NotBeNull();
    }

    [Fact]
    public async Task AdditiveGesture_ForgetsTheFence()
    {
        using var h = new Harness();
        await h.GestureAsync(Box(), "a");

        await h.GestureAsync(Box(additive: true), "b");

        h.Set.Ids.Should().BeEquivalentTo("a", "b");
        h.Memory.Rings.Should().BeNull();
    }

    [Fact]
    public async Task ReplacingGesture_ThatOverflowsTheSelection_IsNotRemembered()
    {
        using var h = new Harness(maxCount: 1);

        await h.GestureAsync(Box(), "a", "b");

        h.Set.Ids.Should().ContainSingle();
        h.Memory.Rings.Should().BeNull();
    }

    [Fact]
    public async Task ClickToggleAfterAFence_ForgetsIt()
    {
        using var h = new Harness();
        await h.GestureAsync(Box(), "a");
        await h.Sut.SetToolAsync(MapSelectionTool.ClickToggle);

        h.Sut.HandleClick("b");

        h.Memory.Rings.Should().BeNull();
    }

    [Fact]
    public async Task ClearAfterAFence_ForgetsIt()
    {
        using var h = new Harness();
        await h.GestureAsync(Box(), "a");

        h.Sut.Clear();

        h.Memory.Rings.Should().BeNull();
    }

    [Fact]
    public async Task FailedGesture_LeavesThePreviousFenceAsItWas()
    {
        using var h = new Harness();
        await h.GestureAsync(Box(), "a");
        var remembered = h.Memory.Rings;

        var handled = h.Sut.HandleGestureAsync(Fence());
        h.Resolver.Calls[^1].SetException(new HttpRequestException("down"));
        await handled;

        h.Memory.Rings.Should().BeSameAs(remembered);
    }

    [Fact]
    public async Task SupersededGesture_IsNotRemembered_EvenWhenItsResultArrivesLast()
    {
        using var h = new Harness();

        var first = h.Sut.HandleGestureAsync(Fence());
        var second = h.Sut.HandleGestureAsync(Box());
        h.Resolver.Calls[1].SetResult(["b"]);
        await second;
        var boxRings = h.Memory.Rings;
        h.Resolver.Calls[0].SetResult(["a"]);
        await first;

        h.Set.Ids.Should().BeEquivalentTo("b");
        h.Memory.Rings.Should().BeSameAs(boxRings);
        boxRings![0].Should().HaveCount(5);
    }

    [Fact]
    public async Task GestureTheNormaliserRejects_ReplacesTheSelection_ButIsNotRemembered()
    {
        using var h = new Harness();
        var degenerate = new MapSelectionGesture(MapSelectionTool.Fence, [[[-70, 18], [-70, 18], [-70, 18]]], Additive: false);

        await h.GestureAsync(degenerate, "a");

        h.Set.Ids.Should().BeEquivalentTo("a");
        h.Memory.Rings.Should().BeNull();
    }

    [Fact]
    public async Task CoordinatorWithoutAMemory_StillReplacesTheSelection()
    {
        var set = new MapSelectionSet();
        var resolver = new ControllableResolver();
        using var sut = new MapSelectionCoordinator(set, resolver, new SilentInterop(), NullLogger<MapSelectionCoordinator>.Instance, DivId);

        var handled = sut.HandleGestureAsync(Box());
        resolver.Calls[0].SetResult(["a"]);
        await handled;

        set.Ids.Should().BeEquivalentTo("a");
    }
}
