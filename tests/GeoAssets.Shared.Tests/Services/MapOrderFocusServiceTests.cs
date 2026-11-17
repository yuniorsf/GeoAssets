using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>
/// <see cref="MapOrderFocusService"/> (XD01-311): fit-to-order through
/// <see cref="IServiceOrderTargetReader.GetExtentAsync"/> (MAP-B24) and the fence outline of an
/// order's selection, against recording fakes of the ports and of the map.
/// </summary>
public class MapOrderFocusServiceTests
{
    private const string DivId = "map-under-test";
    private static readonly DateTime ExecutedAt = new(2026, 11, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly double[][][] Square =
    [
        [[-46.70, -23.60], [-46.60, -23.60], [-46.60, -23.50], [-46.70, -23.50], [-46.70, -23.60]]
    ];

    private sealed class FixedMapContext : ICurrentMapContext
    {
        public string MapDivId => DivId;
    }

    /// <summary>Only <see cref="FitBoundsAsync"/> is reachable from the service; it records every call.</summary>
    private sealed class RecordingMap : IMapSelectionInterop
    {
        public List<string> Calls { get; } = [];
        public List<(string DivId, double[][][] Rings)> Outlines { get; } = [];

        public Task SetFeatureStatesAsync(string divId, IReadOnlyCollection<string> featureIds, MapFeatureState state) => throw new NotSupportedException();
        public Task ClearFeatureStatesAsync(string divId) => throw new NotSupportedException();
        public Task SetSelectionToolAsync(string divId, MapSelectionTool tool) => throw new NotSupportedException();

        public Task ShowSelectionOutlineAsync(string divId, double[][][] rings)
        {
            Calls.Add($"show:{divId}");
            Outlines.Add((divId, rings));
            return Task.CompletedTask;
        }

        public Task ClearSelectionOutlineAsync(string divId)
        {
            Calls.Add($"clear:{divId}");
            return Task.CompletedTask;
        }
    }

    private sealed class StubTargets(Func<string, OrderTargetExtent?> extentOf) : IServiceOrderTargetReader
    {
        public List<(string OrderId, CancellationToken Token)> ExtentCalls { get; } = [];

        public Task<OrderTargetExtent?> GetExtentAsync(string orderId, CancellationToken ct)
        {
            ExtentCalls.Add((orderId, ct));
            return Task.FromResult(extentOf(orderId));
        }

        public Task<PagedResult<ServiceOrderTarget>> GetPageAsync(OrderTargetQuery query, CancellationToken ct) => throw new NotSupportedException();
        public Task<ServiceOrderTarget?> GetByIdAsync(string orderId, string targetId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CountAsync(string orderId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrderTargetUsage>> FindOpenUsagesAsync(IReadOnlyCollection<string> featureIds, string excludingOrderId, CancellationToken ct) => throw new NotSupportedException();
    }

    /// <summary>Answers <see cref="GetExtentAsync"/> with a task the test completes itself.</summary>
    private sealed class PendingTargets(Task<OrderTargetExtent?> extent) : IServiceOrderTargetReader
    {
        public Task<OrderTargetExtent?> GetExtentAsync(string orderId, CancellationToken ct) => extent;
        public Task<PagedResult<ServiceOrderTarget>> GetPageAsync(OrderTargetQuery query, CancellationToken ct) => throw new NotSupportedException();
        public Task<ServiceOrderTarget?> GetByIdAsync(string orderId, string targetId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> CountAsync(string orderId, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrderTargetUsage>> FindOpenUsagesAsync(IReadOnlyCollection<string> featureIds, string excludingOrderId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class StubOrders(params ServiceOrder[] orders) : IServiceOrderReader
    {
        public List<(string OrderId, CancellationToken Token)> Calls { get; } = [];

        public Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            Calls.Add((id, ct));
            return Task.FromResult<IServiceOrder?>(orders.FirstOrDefault(o => string.Equals(o.Id, id, StringComparison.Ordinal)));
        }

        public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(string targetId, DispatchTargetType targetType, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ListLogger : ILogger<MapOrderFocusService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class Harness
    {
        public RecordingMapInterop Map { get; } = new();
        public RecordingMap Overlay { get; } = new();
        public ListLogger Logger { get; } = new();
        public StubTargets Targets { get; }
        public StubOrders Orders { get; }
        public MapOrderFocusService Sut { get; }

        public Harness(Func<string, OrderTargetExtent?>? extentOf = null, params ServiceOrder[] orders)
        {
            Targets = new StubTargets(extentOf ?? (_ => null));
            Orders = new StubOrders(orders);
            Sut = new MapOrderFocusService(Orders, Targets, Map, Overlay, new FixedMapContext(), Logger);
        }
    }

    private static FeatureSelectionSpec FenceSpec(object rings) => new()
    {
        StrategyId = "polygon-fence",
        Parameters = new Dictionary<string, object>(StringComparer.Ordinal) { ["rings"] = rings },
        ExecutedAt = ExecutedAt,
    };

    private static ServiceOrder Order(string id, FeatureSelectionSpec? spec) => new() { Id = id, SelectionSpec = spec };

    // ── Fit to order (MAP-B24) ───────────────────────────────────────────────

    [Fact]
    public async Task MAP_B24_FitToExtent_Null_IsANoOp()
    {
        var h = new Harness();

        (await h.Sut.FitToExtentAsync(null)).Should().BeFalse();

        h.Map.FitCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task MAP_B24_FitToExtent_NormalExtent_FitsTheCurrentMapWithALonLatBox()
    {
        var h = new Harness();

        (await h.Sut.FitToExtentAsync(new OrderTargetExtent(-46.70, -23.60, -46.60, -23.50))).Should().BeTrue();

        var call = h.Map.FitCalls.Should().ContainSingle().Subject;
        call.DivId.Should().Be(DivId);
        call.Bbox.Should().Equal(-46.70, -23.60, -46.60, -23.50);
    }

    [Fact]
    public async Task MAP_B24_FitToExtent_SinglePoint_IsPaddedBeforeFitting()
    {
        var h = new Harness();

        (await h.Sut.FitToExtentAsync(new OrderTargetExtent(-46.633, -23.55, -46.633, -23.55))).Should().BeTrue();

        var bbox = h.Map.FitCalls.Should().ContainSingle().Subject.Bbox;
        (bbox[2] - bbox[0]).Should().BeApproximately(MapExtentFitter.MinSpanDegrees, 1e-9);
        (bbox[3] - bbox[1]).Should().BeApproximately(MapExtentFitter.MinSpanDegrees, 1e-9);
        ((bbox[0] + bbox[2]) / 2).Should().BeApproximately(-46.633, 1e-9);
        ((bbox[1] + bbox[3]) / 2).Should().BeApproximately(-23.55, 1e-9);
    }

    [Fact]
    public async Task FitToOrder_ReadsTheExtentThroughThePort_ForwardsTheToken_AndFits()
    {
        var h = new Harness(id => string.Equals(id, "so-1", StringComparison.Ordinal) ? new OrderTargetExtent(-46.70, -23.60, -46.60, -23.50) : null);
        using var cts = new CancellationTokenSource();

        (await h.Sut.FitToOrderAsync("so-1", cts.Token)).Should().BeTrue();

        h.Targets.ExtentCalls.Should().Equal(("so-1", cts.Token));
        h.Map.FitCalls.Should().ContainSingle().Which.Bbox.Should().Equal(-46.70, -23.60, -46.60, -23.50);
    }

    [Fact]
    public async Task FitToOrder_OrderWithoutPositionedTargets_LeavesTheViewAlone()
    {
        var h = new Harness(_ => null);

        (await h.Sut.FitToOrderAsync("so-empty", CancellationToken.None)).Should().BeFalse();

        h.Targets.ExtentCalls.Should().ContainSingle();
        h.Map.FitCalls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task FitToOrder_BlankOrderId_ThrowsWithoutReadingOrFitting(string orderId)
    {
        var h = new Harness();

        var act = () => h.Sut.FitToOrderAsync(orderId, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        h.Targets.ExtentCalls.Should().BeEmpty();
        h.Map.FitCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task FitToOrder_CancelledWhileReading_DoesNotMoveTheMap()
    {
        // The read completes only after the caller has cancelled, as a slow request would.
        using var cts = new CancellationTokenSource();
        var reading = new TaskCompletionSource<OrderTargetExtent?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var h = new Harness();
        var sut = new MapOrderFocusService(h.Orders, new PendingTargets(reading.Task), h.Map, h.Overlay, new FixedMapContext(), h.Logger);

        var fit = sut.FitToOrderAsync("so-1", cts.Token);
        await cts.CancelAsync();
        reading.SetResult(new OrderTargetExtent(0, 0, 1, 1));

        var act = () => fit;
        await act.Should().ThrowAsync<OperationCanceledException>();
        h.Map.FitCalls.Should().BeEmpty();
    }

    // ── Fence outline ────────────────────────────────────────────────────────

    [Fact]
    public async Task ShowFenceOutline_SpecWithRings_DrawsThemOnTheCurrentMap()
    {
        var h = new Harness();

        (await h.Sut.ShowFenceOutlineAsync(FenceSpec(Square))).Should().BeTrue();

        h.Overlay.Calls.Should().Equal($"show:{DivId}");
        h.Overlay.Outlines.Single().Rings.Should().BeEquivalentTo(Square, o => o.WithStrictOrdering());
        h.Logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ShowFenceOutline_NoSpec_ClearsAnyOutline_WithoutLogging()
    {
        var h = new Harness();

        (await h.Sut.ShowFenceOutlineAsync(null)).Should().BeFalse();

        h.Overlay.Calls.Should().Equal($"clear:{DivId}");
        h.Logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ShowFenceOutline_OtherStrategy_ClearsAnyOutline_WithoutLoggingOrThrowing()
    {
        var h = new Harness();
        var spec = new FeatureSelectionSpec
        {
            StrategyId = "identifier-list",
            Parameters = new Dictionary<string, object>(StringComparer.Ordinal) { ["ids"] = new[] { "a", "b" } },
            ExecutedAt = ExecutedAt,
        };

        (await h.Sut.ShowFenceOutlineAsync(spec)).Should().BeFalse();

        h.Overlay.Calls.Should().Equal($"clear:{DivId}");
        h.Logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ShowFenceOutline_MalformedRings_ClearsAnyOutline_AndLogsAWarning_NotThrown()
    {
        var h = new Harness();

        (await h.Sut.ShowFenceOutlineAsync(FenceSpec("not rings"))).Should().BeFalse();

        h.Overlay.Calls.Should().Equal($"clear:{DivId}");
        var entry = h.Logger.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("not shown");
    }

    [Fact]
    public async Task ShowOrderFenceOutline_LoadsTheOrder_ForwardsTheToken_AndOutlinesItsFence()
    {
        var h = new Harness(null, Order("so-fence", FenceSpec(Square)), Order("so-list", null));
        using var cts = new CancellationTokenSource();

        (await h.Sut.ShowOrderFenceOutlineAsync("so-fence", cts.Token)).Should().BeTrue();

        h.Orders.Calls.Should().Equal(("so-fence", cts.Token));
        h.Overlay.Calls.Should().Equal($"show:{DivId}");
    }

    [Theory]
    [InlineData("so-list")]
    [InlineData("so-unknown")]
    public async Task ShowOrderFenceOutline_OrderWithoutAFence_OrUnknownOrder_ClearsTheOutline(string orderId)
    {
        var h = new Harness(null, Order("so-fence", FenceSpec(Square)), Order("so-list", null));

        (await h.Sut.ShowOrderFenceOutlineAsync(orderId, CancellationToken.None)).Should().BeFalse();

        h.Overlay.Calls.Should().Equal($"clear:{DivId}");
        h.Logger.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ShowOrderFenceOutline_BlankOrderId_Throws()
    {
        var h = new Harness();

        var act = () => h.Sut.ShowOrderFenceOutlineAsync(" ", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        h.Orders.Calls.Should().BeEmpty();
        h.Overlay.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ClearFenceOutline_ClearsTheCurrentMap()
    {
        var h = new Harness();

        await h.Sut.ClearFenceOutlineAsync();

        h.Overlay.Calls.Should().Equal($"clear:{DivId}");
    }

    [Fact]
    public async Task FenceOutline_NeverTouchesFeaturesStatesOrTheView()
    {
        // RecordingMap throws for every feature-state / tool member and RecordingMapInterop for every
        // IMapInterop member but FitBoundsAsync, so reaching here proves none was called.
        var h = new Harness(null, Order("so-fence", FenceSpec(Square)));

        await h.Sut.ShowOrderFenceOutlineAsync("so-fence", CancellationToken.None);
        await h.Sut.ClearFenceOutlineAsync();

        h.Map.FitCalls.Should().BeEmpty();
    }
}
