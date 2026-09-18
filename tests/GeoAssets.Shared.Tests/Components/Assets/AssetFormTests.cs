using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Shared.Tests;
using GeoAssets.Shared.Components.Assets;
using GeoAssets.Shared.Components.Map;
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

    // ── FindAutoLinkCandidates (plural — XD01-152) ─────────────────────────────

    [Fact]
    public void FindAutoLinkCandidates_NullGeometry_ReturnsEmpty()
    {
        var result = AssetForm.FindAutoLinkCandidates(null, nearby: [], intersecting: []);

        result.Should().BeEmpty();
    }

    [Fact]
    public void FindAutoLinkCandidates_GeometryNotAPoint_ReturnsEmpty()
    {
        var line = new GeoLineString([(0, 0), (1, 1)]);
        var wire = LineFeature("wire-1", (0, 0), (1, 1));

        var result = AssetForm.FindAutoLinkCandidates(line, nearby: [wire], intersecting: []);

        result.Should().BeEmpty();
    }

    [Fact]
    public void FindAutoLinkCandidates_TwoLineStringCandidates_ReturnsBoth()
    {
        // The behavior FindAutoLinkCandidate (singular) intentionally throws away — this is what
        // AssetForm.OnParametersSet surfaces to the multi-candidate picker instead.
        var point = new GeoPoint(0, 0);
        var wireA = LineFeature("wire-a", (0, 0), (1, 1));
        var wireB = LineFeature("wire-b", (0, 0), (-1, -1));

        var result = AssetForm.FindAutoLinkCandidates(point, nearby: [wireA, wireB], intersecting: []);

        result.Should().BeEquivalentTo([wireA, wireB]);
    }

    [Fact]
    public void FindAutoLinkCandidates_SameCandidateInBothLists_DeduplicatedToOne()
    {
        var point = new GeoPoint(0, 0);
        var wire = LineFeature("wire-1", (0, 0), (1, 1));

        var result = AssetForm.FindAutoLinkCandidates(point, nearby: [wire], intersecting: [wire]);

        result.Should().ContainSingle().Which.Should().BeSameAs(wire);
    }

    [Fact]
    public void FindAutoLinkCandidates_NonLineStringCandidatesAreIgnored()
    {
        var point = new GeoPoint(0, 0);
        var wireA = LineFeature("wire-a", (0, 0), (1, 1));
        var otherPoint = PointFeature("pole-1", 0, 0);

        var result = AssetForm.FindAutoLinkCandidates(point, nearby: [wireA, otherPoint], intersecting: []);

        result.Should().ContainSingle().Which.Should().BeSameAs(wireA);
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

    // ── End-to-end: multi-candidate picker confirms create the chosen TopoEdges (XD01-152) ────

    [Fact]
    public void TwoCandidates_ConfirmedSingleViaPicker_CreatesOneTopoEdge_VerifiableViaGetNeighbors()
    {
        var repository = new TestAssetProvider();
        var wireA = LineFeature("wire-a", (-0.0001, -0.0001), (0.0001, 0.0001));
        var wireB = LineFeature("wire-b", (-0.0001, 0.0001), (0.0001, -0.0001));
        repository.Add(wireA);
        repository.Add(wireB);

        var pole = PointFeature("pole-1", 0, 0);
        var geometry = (GeoPoint)pole.Geometry!;
        var candidates = AssetForm.FindAutoLinkCandidates(
            geometry,
            repository.GetNearby(geometry, AssetForm.SnapDistanceDegrees),
            repository.GetIntersecting(geometry));
        candidates.Should().HaveCount(2);
        repository.Add(pole); // asset saves immediately; edges are added post-confirm (XD01-151)

        var picker = new CandidatePickerState(candidates);
        picker.CycleNext(); // land on wire-b
        var chosen = picker.Confirm(); // Single mode: only the current candidate

        var saved = repository.GetById(pole.Id)!;
        foreach (var c in chosen)
            saved.Topology.Add(new TopoEdge { TargetId = c.Id, Kind = "connected-to", Weight = 1.0 });
        repository.Update(saved);

        repository.GetNeighbors(pole.Id).Should().ContainSingle().Which.Id.Should().Be(wireB.Id);
    }

    [Fact]
    public void TwoCandidates_ConfirmedMultipleViaPicker_CreatesTopoEdgeForEachChosenCandidate()
    {
        var repository = new TestAssetProvider();
        var wireA = LineFeature("wire-a", (-0.0001, -0.0001), (0.0001, 0.0001));
        var wireB = LineFeature("wire-b", (-0.0001, 0.0001), (0.0001, -0.0001));
        repository.Add(wireA);
        repository.Add(wireB);

        var pole = PointFeature("pole-1", 0, 0);
        var geometry = (GeoPoint)pole.Geometry!;
        var candidates = AssetForm.FindAutoLinkCandidates(
            geometry,
            repository.GetNearby(geometry, AssetForm.SnapDistanceDegrees),
            repository.GetIntersecting(geometry));
        repository.Add(pole);

        var picker = new CandidatePickerState(candidates);
        picker.SwitchMode(CandidatePickerMode.Multiple); // carries over the current candidate
        picker.CycleNext();
        picker.ToggleCurrentSelected(); // both candidates now selected
        var chosen = picker.Confirm();
        chosen.Should().HaveCount(2);

        var saved = repository.GetById(pole.Id)!;
        foreach (var c in chosen)
            saved.Topology.Add(new TopoEdge { TargetId = c.Id, Kind = "connected-to", Weight = 1.0 });
        repository.Update(saved);

        repository.GetNeighbors(pole.Id).Should().HaveCount(2)
            .And.Contain(f => f.Id == wireA.Id)
            .And.Contain(f => f.Id == wireB.Id);
    }

    [Fact]
    public void TwoCandidates_PickerCancelledWithoutConfirm_CreatesNoTopoEdge()
    {
        // Mirrors the abandon-path decision from XD01-151: declining/dismissing the picker
        // leaves behavior equivalent to today's no-op — no edge is ever added because Confirm()
        // is simply never called.
        var repository = new TestAssetProvider();
        repository.Add(LineFeature("wire-a", (-0.0001, -0.0001), (0.0001, 0.0001)));
        repository.Add(LineFeature("wire-b", (-0.0001, 0.0001), (0.0001, -0.0001)));

        var pole = PointFeature("pole-1", 0, 0);
        var geometry = (GeoPoint)pole.Geometry!;
        var candidates = AssetForm.FindAutoLinkCandidates(
            geometry,
            repository.GetNearby(geometry, AssetForm.SnapDistanceDegrees),
            repository.GetIntersecting(geometry));
        repository.Add(pole);

        _ = new CandidatePickerState(candidates); // picker opened, then abandoned — never confirmed

        repository.GetNeighbors(pole.Id).Should().BeEmpty();
    }
}
