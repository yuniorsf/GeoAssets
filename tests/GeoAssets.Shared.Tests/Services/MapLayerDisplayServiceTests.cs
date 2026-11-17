using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>
/// <see cref="MapLayerDisplayService"/> (XD01-311): which <c>SetLayerZoomRangeAsync</c> /
/// <c>SetLayerVisibilityAsync</c> calls reach the map when asset types are loaded, changed or
/// toggled, and that the user's toggle and the zoom range are tracked independently.
/// </summary>
public class MapLayerDisplayServiceTests
{
    private const string DivId = "map1";

    private sealed class ListLogger : ILogger<MapLayerDisplayService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }

    private static readonly AssetType Open = new() { Name = "Open" };
    private static readonly AssetType Bounded = new() { Name = "Bounded", MinZoom = 13, MaxZoom = 15 };
    private static readonly AssetType MinOnly = new() { Name = "MinOnly", MinZoom = 15 };
    private static readonly AssetType MaxOnly = new() { Name = "MaxOnly", MaxZoom = 12 };

    private static (MapLayerDisplayService Sut, RecordingMapInterop Map, ListLogger Logger) Create()
    {
        var map = new RecordingMapInterop();
        var logger = new ListLogger();
        return (new MapLayerDisplayService(map, logger), map, logger);
    }

    private static string Id(AssetType type) => type.Id.ToString();

    // ── Zoom ranges ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Sync_OnAFreshMap_PushesOnlyTheTypesThatHaveARange_WithTheirBounds()
    {
        var (sut, map, _) = Create();

        await sut.SyncZoomRangesAsync(DivId, [Open, Bounded, MinOnly, MaxOnly]);

        map.ZoomRangeCalls.Should().BeEquivalentTo(new (string, string, int?, int?)[]
        {
            (DivId, Id(Bounded), 13, 15),
            (DivId, Id(MinOnly), 15, null),
            (DivId, Id(MaxOnly), null, 12),
        });
    }

    [Fact]
    public async Task Sync_WithOnlyOpenTypes_MakesNoCall()
    {
        var (sut, map, _) = Create();

        await sut.SyncZoomRangesAsync(DivId, [.. AssetType.Defaults, Open]);

        map.ZoomRangeCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_Repeated_PushesNothingTheSecondTime()
    {
        var (sut, map, _) = Create();
        await sut.SyncZoomRangesAsync(DivId, [Open, Bounded]);
        map.ZoomRangeCalls.Clear();

        await sut.SyncZoomRangesAsync(DivId, [Open, Bounded]);

        map.ZoomRangeCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_AfterARangeChanged_PushesOnlyThatType()
    {
        var (sut, map, _) = Create();
        var changing = new AssetType { Name = "Changing", MinZoom = 10 };
        await sut.SyncZoomRangesAsync(DivId, [Bounded, changing]);
        map.ZoomRangeCalls.Clear();

        changing.MinZoom = 11;
        changing.MaxZoom = 18;
        await sut.SyncZoomRangesAsync(DivId, [Bounded, changing]);

        map.ZoomRangeCalls.Should().Equal((DivId, Id(changing), 11, 18));
    }

    [Fact]
    public async Task Sync_AfterATypeLostItsRange_OrDisappeared_ClearsItOnTheMapOnce()
    {
        var (sut, map, _) = Create();
        var losing = new AssetType { Name = "Losing", MaxZoom = 9 };
        await sut.SyncZoomRangesAsync(DivId, [Bounded, losing]);
        map.ZoomRangeCalls.Clear();

        losing.MaxZoom = null;
        await sut.SyncZoomRangesAsync(DivId, [losing]); // Bounded is gone (deleted, or another provider's types)

        map.ZoomRangeCalls.Should().BeEquivalentTo(new (string, string, int?, int?)[]
        {
            (DivId, Id(Bounded), null, null),
            (DivId, Id(losing), null, null),
        });

        map.ZoomRangeCalls.Clear();
        await sut.SyncZoomRangesAsync(DivId, [losing]);
        map.ZoomRangeCalls.Should().BeEmpty();
    }

    [Fact]
    public async Task Sync_SwitchingToAProviderWhoseSameTypeIdHasAnotherRange_PushesTheNewOne()
    {
        var (sut, map, _) = Create();
        var here = new AssetType { Id = AssetType.Point.Id, Name = "Point", MinZoom = 14 };
        var there = new AssetType { Id = AssetType.Point.Id, Name = "Point" };
        await sut.SyncZoomRangesAsync(DivId, [here]);
        map.ZoomRangeCalls.Clear();

        await sut.SyncZoomRangesAsync(DivId, [there]);

        map.ZoomRangeCalls.Should().Equal((DivId, Id(there), null, null));
    }

    [Theory]
    [InlineData(-1, null)]
    [InlineData(null, 25)]
    [InlineData(16, 15)]
    public async Task Sync_StoredRangeThatBreaksTheRules_IsLoggedAndTreatedAsOpen_NotThrown(int? min, int? max)
    {
        var (sut, map, logger) = Create();
        var broken = new AssetType { Name = "Broken", MinZoom = min, MaxZoom = max };

        await sut.SyncZoomRangesAsync(DivId, [broken, Bounded]);

        map.ZoomRangeCalls.Should().Equal((DivId, Id(Bounded), 13, 15));
        logger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task Sync_FailedPush_IsLogged_DoesNotStopTheOthers_AndIsRetriedNextTime()
    {
        var (sut, map, logger) = Create();
        map.FailZoomRangeFor.Add(Id(Bounded));

        await sut.SyncZoomRangesAsync(DivId, [Bounded, MinOnly]);

        map.ZoomRangeCalls.Should().Equal((DivId, Id(MinOnly), 15, null));
        logger.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);

        map.FailZoomRangeFor.Clear();
        map.ZoomRangeCalls.Clear();
        await sut.SyncZoomRangesAsync(DivId, [Bounded, MinOnly]);

        map.ZoomRangeCalls.Should().Equal((DivId, Id(Bounded), 13, 15));
    }

    [Fact]
    public async Task Forget_MakesTheNextSyncPushEverythingAgain_AsAReinitializedMapNeeds()
    {
        var (sut, map, _) = Create();
        await sut.SyncZoomRangesAsync(DivId, [Bounded]);
        map.ZoomRangeCalls.Clear();

        sut.Forget(DivId);
        await sut.SyncZoomRangesAsync(DivId, [Bounded]);

        map.ZoomRangeCalls.Should().Equal((DivId, Id(Bounded), 13, 15));
    }

    [Fact]
    public async Task Sync_KeepsEachMapSeparate()
    {
        var (sut, map, _) = Create();
        await sut.SyncZoomRangesAsync("a", [Bounded]);
        map.ZoomRangeCalls.Clear();

        await sut.SyncZoomRangesAsync("b", [Bounded]);

        map.ZoomRangeCalls.Should().Equal(("b", Id(Bounded), 13, 15));
    }

    [Fact]
    public async Task Sync_BlankDivOrNullTypes_Throws()
    {
        var (sut, _, _) = Create();

        await ((Func<Task>)(() => sut.SyncZoomRangesAsync(" ", []))).Should().ThrowAsync<ArgumentException>();
        await ((Func<Task>)(() => sut.SyncZoomRangesAsync(DivId, null!))).Should().ThrowAsync<ArgumentNullException>();
    }

    // ── User visibility ──────────────────────────────────────────────────────

    [Fact]
    public void IsVisible_DefaultsToTrue_ForAnyMapAndType() =>
        Create().Sut.IsVisible(DivId, "anything").Should().BeTrue();

    [Fact]
    public async Task SetVisible_TellsTheMap_AndIsRememberedPerMap()
    {
        var (sut, map, _) = Create();

        await sut.SetVisibleAsync(DivId, "t1", false);

        map.VisibilityCalls.Should().Equal((DivId, "t1", false));
        sut.IsVisible(DivId, "t1").Should().BeFalse();
        sut.IsVisible(DivId, "t2").Should().BeTrue();
        sut.IsVisible("other-map", "t1").Should().BeTrue();

        await sut.SetVisibleAsync(DivId, "t1", true);

        map.VisibilityCalls.Should().HaveCount(2).And.HaveElementAt(1, (DivId, "t1", true));
        sut.IsVisible(DivId, "t1").Should().BeTrue();
    }

    [Fact]
    public async Task UserToggle_AndZoomRange_AreIndependent_NeitherCallReplacesTheOther()
    {
        var (sut, map, _) = Create();

        // Hidden by the user, then given a range: the map gets one call of each kind and is
        // left to compose them; the user's choice is still recorded afterwards.
        await sut.SetVisibleAsync(DivId, Id(Bounded), false);
        await sut.SyncZoomRangesAsync(DivId, [Bounded]);

        map.VisibilityCalls.Should().Equal((DivId, Id(Bounded), false));
        map.ZoomRangeCalls.Should().Equal((DivId, Id(Bounded), 13, 15));
        sut.IsVisible(DivId, Id(Bounded)).Should().BeFalse();

        // Shown again by the user: no zoom-range call is repeated, the range stays as pushed.
        await sut.SetVisibleAsync(DivId, Id(Bounded), true);
        await sut.SyncZoomRangesAsync(DivId, [Bounded]);

        map.ZoomRangeCalls.Should().HaveCount(1);
        sut.IsVisible(DivId, Id(Bounded)).Should().BeTrue();
    }

    [Fact]
    public async Task Forget_AlsoDropsTheUserToggles_BecauseAFreshMapShowsEverything()
    {
        var (sut, _, _) = Create();
        await sut.SetVisibleAsync(DivId, "t1", false);

        sut.Forget(DivId);

        sut.IsVisible(DivId, "t1").Should().BeTrue();
    }

    [Theory]
    [InlineData("", "t1")]
    [InlineData("map1", " ")]
    public async Task SetVisible_BlankArgument_ThrowsWithoutCallingTheMap(string divId, string assetTypeId)
    {
        var (sut, map, _) = Create();

        var act = () => sut.SetVisibleAsync(divId, assetTypeId, false);

        await act.Should().ThrowAsync<ArgumentException>();
        map.VisibilityCalls.Should().BeEmpty();
    }
}
