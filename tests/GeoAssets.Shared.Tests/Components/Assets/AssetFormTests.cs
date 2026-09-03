using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Shared.Tests;
using GeoAssets.Shared.Components.Assets;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Assets;

/// <summary>
/// <see cref="AssetForm.FindAutoLinkCandidate"/> is the pure v1 auto-link heuristic at the heart
/// of XD01-118 — factored out as a static method so it's directly unit-testable without a Blazor
/// render tree (this repo has no bUnit yet; matches the pattern already used by
/// AssetsTable/DrawToolbar/MapContainer). A second group of tests exercises the acceptance
/// criterion end-to-end (a TopoEdge is persisted and retrievable via
/// <see cref="IAssetProvider.GetNeighbors"/>) by composing the same
/// building blocks <c>AssetForm.HandleSave</c> does, against a real <see cref="TestAssetProvider"/>.
/// </summary>
public class AssetFormTests
{
    private static GeoFeature LineFeature(string id, params (double Lon, double Lat)[] points) => new()
    {
        Id = id,
        Geometry = new GeoLineString(points)
    };

    private static GeoFeature PointFeature(string id, double lon, double lat) => new()
    {
        Id = id,
        Geometry = new GeoPoint(lon, lat)
    };

    // ── FindAutoLinkCandidate ────────────────────────────────────────────────

    [Fact]
    public void FindAutoLinkCandidate_NullGeometry_ReturnsNull()
    {
        var result = AssetForm.FindAutoLinkCandidate(null, nearby: [], intersecting: []);

        result.Should().BeNull();
    }

    [Fact]
    public void FindAutoLinkCandidate_GeometryNotAPoint_ReturnsNull()
    {
        var line = new GeoLineString([(0, 0), (1, 1)]);
        var wire = LineFeature("wire-1", (0, 0), (1, 1));

        var result = AssetForm.FindAutoLinkCandidate(line, nearby: [wire], intersecting: []);

        result.Should().BeNull();
    }

    [Fact]
    public void FindAutoLinkCandidate_NoCandidates_ReturnsNull()
    {
        var point = new GeoPoint(0, 0);

        var result = AssetForm.FindAutoLinkCandidate(point, nearby: [], intersecting: []);

        result.Should().BeNull();
    }

    [Fact]
    public void FindAutoLinkCandidate_ExactlyOneLineStringNearby_ReturnsIt()
    {
        var point = new GeoPoint(0, 0);
        var wire = LineFeature("wire-1", (0, 0), (1, 1));

        var result = AssetForm.FindAutoLinkCandidate(point, nearby: [wire], intersecting: []);

        result.Should().BeSameAs(wire);
    }

    [Fact]
    public void FindAutoLinkCandidate_ExactlyOneLineStringIntersecting_ReturnsIt()
    {
        var point = new GeoPoint(0, 0);
        var wire = LineFeature("wire-1", (0, 0), (1, 1));

        var result = AssetForm.FindAutoLinkCandidate(point, nearby: [], intersecting: [wire]);

        result.Should().BeSameAs(wire);
    }

    [Fact]
    public void FindAutoLinkCandidate_SameCandidateInBothLists_DeduplicatedToOne()
    {
        // Proves the union of nearby+intersecting doesn't double-count the same feature
        // appearing in both (e.g. a point placed exactly on the line).
        var point = new GeoPoint(0, 0);
        var wire = LineFeature("wire-1", (0, 0), (1, 1));

        var result = AssetForm.FindAutoLinkCandidate(point, nearby: [wire], intersecting: [wire]);

        result.Should().BeSameAs(wire);
    }

    [Fact]
    public void FindAutoLinkCandidate_TwoLineStringCandidates_ReturnsNull()
    {
        // v1 scope: 2+ candidates take no automatic action (XD01-120 tracks the picker).
        var point = new GeoPoint(0, 0);
        var wireA = LineFeature("wire-a", (0, 0), (1, 1));
        var wireB = LineFeature("wire-b", (0, 0), (-1, -1));

        var result = AssetForm.FindAutoLinkCandidate(point, nearby: [wireA, wireB], intersecting: []);

        result.Should().BeNull();
    }

    [Fact]
    public void FindAutoLinkCandidate_NonLineStringCandidatesAreIgnored()
    {
        var point = new GeoPoint(0, 0);
        var otherPoint = PointFeature("pole-1", 0, 0);

        var result = AssetForm.FindAutoLinkCandidate(point, nearby: [otherPoint], intersecting: []);

        result.Should().BeNull();
    }

    // ── End-to-end: TopoEdge persisted and retrievable via GetNeighbors ────────

    [Fact]
    public void AutoLinkedFeature_Saved_CreatesTopoEdge_VerifiableViaGetNeighbors()
    {
        var repository = new TestAssetProvider();
        var wire = LineFeature("wire-1", (-0.0001, -0.0001), (0.0001, 0.0001));
        repository.Add(wire);

        var pole = PointFeature("pole-1", 0, 0);
        var geometry = (GeoPoint)pole.Geometry!;
        var candidate = AssetForm.FindAutoLinkCandidate(
            geometry,
            repository.GetNearby(geometry, AssetForm.SnapDistanceDegrees),
            repository.GetIntersecting(geometry));
        candidate.Should().NotBeNull();

        pole.Topology.Add(new TopoEdge { TargetId = candidate!.Id, Kind = "connected-to", Weight = 1.0 });
        repository.Add(pole);

        repository.GetNeighbors(pole.Id).Should().ContainSingle().Which.Id.Should().Be(wire.Id);
    }

    [Fact]
    public void NoCandidate_Saved_CreatesNoTopoEdge_AndDoesNotError()
    {
        var repository = new TestAssetProvider();
        var pole = PointFeature("pole-1", 50, 50); // far from anything else in the repository

        var geometry = (GeoPoint)pole.Geometry!;
        var candidate = AssetForm.FindAutoLinkCandidate(
            geometry,
            repository.GetNearby(geometry, AssetForm.SnapDistanceDegrees),
            repository.GetIntersecting(geometry));
        candidate.Should().BeNull();

        var act = () => repository.Add(pole);

        act.Should().NotThrow();
        repository.GetNeighbors(pole.Id).Should().BeEmpty();
    }

    [Fact]
    public void TwoCandidates_Saved_CreatesNoTopoEdge_AndDoesNotError()
    {
        var repository = new TestAssetProvider();
        repository.Add(LineFeature("wire-a", (-0.0001, -0.0001), (0.0001, 0.0001)));
        repository.Add(LineFeature("wire-b", (-0.0001, 0.0001), (0.0001, -0.0001)));

        var pole = PointFeature("pole-1", 0, 0);
        var geometry = (GeoPoint)pole.Geometry!;
        var candidate = AssetForm.FindAutoLinkCandidate(
            geometry,
            repository.GetNearby(geometry, AssetForm.SnapDistanceDegrees),
            repository.GetIntersecting(geometry));
        candidate.Should().BeNull();

        var act = () => repository.Add(pole);

        act.Should().NotThrow();
        repository.GetNeighbors(pole.Id).Should().BeEmpty();
    }
}
