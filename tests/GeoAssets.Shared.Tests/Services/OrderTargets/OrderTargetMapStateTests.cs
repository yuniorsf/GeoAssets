using FluentAssertions;
using GeoAssets.Shared.Interfaces;
using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Workflow.Targets;
using Xunit;
using static GeoAssets.Shared.Tests.Services.OrderTargets.TargetWorkFakes;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>How a target of the shown order looks on the map (XD01-313).</summary>
public sealed class OrderTargetMapStateTests
{
    [Theory]
    // not worked: dimmed whatever the conflict
    [InlineData(OrderTargetStatus.Skipped, OrderTargetConflictLevel.None, MapFeatureState.Disabled)]
    [InlineData(OrderTargetStatus.NotExecutable, OrderTargetConflictLevel.Warning, MapFeatureState.Disabled)]
    [InlineData(OrderTargetStatus.Skipped, OrderTargetConflictLevel.Blocked, MapFeatureState.Disabled)]
    // blocked: dimmed, even when done
    [InlineData(OrderTargetStatus.Pending, OrderTargetConflictLevel.Blocked, MapFeatureState.Disabled)]
    [InlineData(OrderTargetStatus.InProgress, OrderTargetConflictLevel.Blocked, MapFeatureState.Disabled)]
    [InlineData(OrderTargetStatus.Done, OrderTargetConflictLevel.Blocked, MapFeatureState.Disabled)]
    // done wins over a warning
    [InlineData(OrderTargetStatus.Done, OrderTargetConflictLevel.None, MapFeatureState.Done)]
    [InlineData(OrderTargetStatus.Done, OrderTargetConflictLevel.Warning, MapFeatureState.Done)]
    // a warning on work still to do
    [InlineData(OrderTargetStatus.Pending, OrderTargetConflictLevel.Warning, MapFeatureState.Conflict)]
    [InlineData(OrderTargetStatus.InProgress, OrderTargetConflictLevel.Warning, MapFeatureState.Conflict)]
    // work still to do
    [InlineData(OrderTargetStatus.Pending, OrderTargetConflictLevel.None, MapFeatureState.Pending)]
    [InlineData(OrderTargetStatus.InProgress, OrderTargetConflictLevel.None, MapFeatureState.Pending)]
    [InlineData("SomethingNew", OrderTargetConflictLevel.None, MapFeatureState.Pending)]
    [InlineData("done", OrderTargetConflictLevel.None, MapFeatureState.Pending)]
    [InlineData(OrderTargetStatus.Pending, (OrderTargetConflictLevel)42, MapFeatureState.Pending)]
    public void Of_DecidesOneStatePerTarget(string status, OrderTargetConflictLevel conflict, MapFeatureState expected) =>
        OrderTargetMapState.Of(Target("f1", status, conflict)).Should().Be(expected);

    [Fact]
    public void Of_Null_Throws() =>
        FluentActions.Invoking(() => OrderTargetMapState.Of(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void AConflictAndABlockedTarget_NeverLookAlike() =>
        OrderTargetMapState.Of(Target("a", conflict: OrderTargetConflictLevel.Warning))
            .Should().NotBe(OrderTargetMapState.Of(Target("b", conflict: OrderTargetConflictLevel.Blocked)));

    [Fact]
    public void Legend_ListsEveryStateOfReturns_InDisplayOrder() =>
        OrderTargetMapState.Legend.Should().Equal(MapFeatureState.Pending, MapFeatureState.Done, MapFeatureState.Conflict, MapFeatureState.Disabled);

    [Theory]
    [InlineData(MapFeatureState.Pending, "targetWork.map.pending")]
    [InlineData(MapFeatureState.Done, "targetWork.map.done")]
    [InlineData(MapFeatureState.Conflict, "targetWork.map.conflict")]
    [InlineData(MapFeatureState.Disabled, "targetWork.map.disabled")]
    [InlineData(MapFeatureState.Default, null)]
    [InlineData(MapFeatureState.Selected, null)]
    [InlineData(MapFeatureState.Highlighted, null)]
    public void LegendKey_OnlyForTargetStates(MapFeatureState state, string? expected) =>
        OrderTargetMapState.LegendKey(state).Should().Be(expected);

    [Theory]
    [InlineData(MapFeatureState.Pending, "order-target-map-swatch order-target-map-swatch-pending")]
    [InlineData(MapFeatureState.Done, "order-target-map-swatch order-target-map-swatch-done")]
    [InlineData(MapFeatureState.Conflict, "order-target-map-swatch order-target-map-swatch-conflict")]
    [InlineData(MapFeatureState.Disabled, "order-target-map-swatch order-target-map-swatch-disabled")]
    public void SwatchClass_NamesTheStatesJsName(MapFeatureState state, string expected) =>
        OrderTargetMapState.SwatchClass(state).Should().Be(expected);

    [Theory]
    [InlineData(MapFeatureState.Pending, "pending")]
    [InlineData(MapFeatureState.Done, "done")]
    public void TheTwoTargetStates_HaveTheirOwnJsNames(MapFeatureState state, string expected) =>
        state.ToJsName().Should().Be(expected);

    [Fact]
    public void EveryLegendState_HasAStyleInTheScript_AndATokenAndSwatchInTheStylesheet()
    {
        var root = RepositoryRoot();
        var script = File.ReadAllText(Path.Combine(root, "apps", "GeoAssets.Shared", "wwwroot", "js", "geoassets.js"));
        var css = File.ReadAllText(Path.Combine(root, "apps", "GeoAssets.Shared", "wwwroot", "css", "geoassets.css"));

        foreach (var state in OrderTargetMapState.Legend)
        {
            var name = state.ToJsName();
            script.Should().Contain($"{name}:", $"_STATE_STYLES must style '{name}'").And.Contain($"'--map-state-{name}'");
            css.Should().Contain($"--map-state-{name}:").And.Contain($".order-target-map-swatch-{name}");
        }

        css.Should().Contain(".leaflet-marker-icon.geoassets-state-pending").And.Contain(".leaflet-marker-icon.geoassets-state-done");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }
}
