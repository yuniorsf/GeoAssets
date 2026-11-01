using FluentAssertions;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.FieldWork;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

/// <summary>
/// XD01-273's AC: "Nearest-available-CTO assignment is unit-tested against a small synthetic
/// CTO/port fixture."
/// </summary>
public class NearestAvailableCtoAssignerTests
{
    private static readonly GeoPoint DropLocation = new(-46.6333, -23.5505);

    // A nearby, fully-occupied CTO; a farther CTO with a free port; a farthest CTO also with a
    // free port — the nearer-but-full one must be skipped in favor of the nearer-with-capacity one.
    private static readonly CtoPortAvailability NearButFull = new("cto-near-full", new GeoPoint(-46.6334, -23.5506), TotalPorts: 8, OccupiedPorts: 8);
    private static readonly CtoPortAvailability MidWithCapacity = new("cto-mid-available", new GeoPoint(-46.64, -23.56), TotalPorts: 16, OccupiedPorts: 12);
    private static readonly CtoPortAvailability FarWithCapacity = new("cto-far-available", new GeoPoint(-46.70, -23.60), TotalPorts: 16, OccupiedPorts: 0);

    [Fact]
    public void FindNearest_SkipsFullCto_ReturnsNearestWithCapacity()
    {
        var candidates = new[] { NearButFull, MidWithCapacity, FarWithCapacity };

        var result = NearestAvailableCtoAssigner.FindNearest(DropLocation, candidates);

        result.Should().Be(MidWithCapacity);
    }

    [Fact]
    public void FindNearest_AllCandidatesFull_ReturnsNull()
    {
        var fullCandidates = new[]
        {
            NearButFull,
            MidWithCapacity with { OccupiedPorts = MidWithCapacity.TotalPorts },
            FarWithCapacity with { OccupiedPorts = FarWithCapacity.TotalPorts },
        };

        NearestAvailableCtoAssigner.FindNearest(DropLocation, fullCandidates).Should().BeNull();
    }

    [Fact]
    public void FindNearest_NoCandidates_ReturnsNull() =>
        NearestAvailableCtoAssigner.FindNearest(DropLocation, []).Should().BeNull();

    [Fact]
    public void FindNearest_SingleAvailableCandidate_ReturnsIt() =>
        NearestAvailableCtoAssigner.FindNearest(DropLocation, [FarWithCapacity]).Should().Be(FarWithCapacity);

    [Fact]
    public void HasAvailablePort_OccupiedEqualsTotal_ReturnsFalse() =>
        NearButFull.HasAvailablePort.Should().BeFalse();

    [Fact]
    public void HasAvailablePort_OccupiedLessThanTotal_ReturnsTrue() =>
        MidWithCapacity.HasAvailablePort.Should().BeTrue();
}
