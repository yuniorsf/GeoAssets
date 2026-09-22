using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Shared.Components.Map;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Map;

/// <summary>
/// Covers <see cref="MapContainer"/>'s pure decisions, factored out as static methods so they're
/// directly unit-testable without a Blazor render tree (this repo has no bUnit yet):
/// <see cref="MapContainer.ResolveDrawnAssetTypeId"/> (XD01-117 — prefer a type-first palette
/// selection, otherwise fall back to the pre-XD01-117 geometry-based inference) and
/// <see cref="MapContainer.ShouldRenderLastViewport"/> (XD01-162 — prefer a bounds-filtered
/// re-render over the last known viewport, otherwise fall back to a full-dataset pipeline render).
/// </summary>
public class MapContainerTests
{
    [Fact]
    public void ResolveDrawnAssetTypeId_PendingTypeSet_TakesPrecedenceOverGeometry()
    {
        // Even for a LineString/Polygon, a pending type-first selection wins — proves this isn't
        // just falling through to the geometry switch by coincidence.
        var result = MapContainer.ResolveDrawnAssetTypeId(new GeoPolygon(), pendingAssetTypeId: "pole-type-id");

        result.Should().Be("pole-type-id");
    }

    [Fact]
    public void ResolveDrawnAssetTypeId_NoPendingType_PointGeometry_FallsBackToGenericPointType()
    {
        var result = MapContainer.ResolveDrawnAssetTypeId(new GeoPoint(0, 0), pendingAssetTypeId: null);

        result.Should().Be(AssetType.Point.Id.ToString());
    }

    [Fact]
    public void ResolveDrawnAssetTypeId_NoPendingType_LineGeometry_FallsBackToGenericLineType()
    {
        var result = MapContainer.ResolveDrawnAssetTypeId(new GeoLineString(), pendingAssetTypeId: null);

        result.Should().Be(AssetType.Line.Id.ToString());
    }

    [Fact]
    public void ResolveDrawnAssetTypeId_NoPendingType_PolygonGeometry_FallsBackToGenericAreaType()
    {
        var result = MapContainer.ResolveDrawnAssetTypeId(new GeoPolygon(), pendingAssetTypeId: null);

        result.Should().Be(AssetType.Area.Id.ToString());
    }

    [Fact]
    public void ResolveDrawnAssetTypeId_NoPendingType_NullGeometry_FallsBackToGenericPointType()
    {
        var result = MapContainer.ResolveDrawnAssetTypeId(null, pendingAssetTypeId: null);

        result.Should().Be(AssetType.Point.Id.ToString());
    }

    // ── ShouldRenderLastViewport (XD01-162) ──────────────────────────────────

    [Fact]
    public void ShouldRenderLastViewport_ViewportKnown_PrefersViewportRender()
    {
        // Literal acceptance criterion: OnCollectionChanged prefers _lastViewport when set.
        MapContainer.ShouldRenderLastViewport((0, 0, 1, 1)).Should().BeTrue();
    }

    [Fact]
    public void ShouldRenderLastViewport_NoViewportYet_FallsBackToFullDatasetPipelineRender()
    {
        MapContainer.ShouldRenderLastViewport(null).Should().BeFalse();
    }
}
