using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Tests;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Selection.Strategies;
using Xunit;

namespace GeoAssets.Workflow.Tests.Selection.Strategies;

/// <summary>
/// Regression tests for the XD01-6 fix: <see cref="FeatureSelectionSpec.Parameters"/> values
/// come back as <see cref="JsonElement"/> after a JSON round-trip (simulating persistence and
/// reload), not their original CLR type. Each test runs a strategy once with fresh parameters
/// and again with round-tripped ones, asserting identical results — proving the strategy no
/// longer throws <see cref="InvalidCastException"/> on a reloaded order.
/// </summary>
public class FeatureSelectionRoundTripTests
{
    private static IReadOnlyDictionary<string, object> RoundTrip(IReadOnlyDictionary<string, object> parameters)
    {
        var json = JsonSerializer.Serialize(parameters);
        return JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;
    }

    private static FeatureSelectionRegistry NewRegistry() =>
        new(TimeProvider.System, "no-such-plugins-dir", typeof(BoundingBoxSelectionStrategy).Assembly);

    // ── bounding-box ─────────────────────────────────────────────────────────

    [Fact]
    public async Task BoundingBox_SurvivesRoundTrip()
    {
        var repo = new TestAssetProvider();
        repo.Add(new GeoFeature { Id = "inside", Geometry = new GeoPoint(0, 0) });
        repo.Add(new GeoFeature { Id = "outside", Geometry = new GeoPoint(50, 50) });

        var parameters = new Dictionary<string, object>
        {
            ["minLon"] = -1.0, ["minLat"] = -1.0, ["maxLon"] = 1.0, ["maxLat"] = 1.0,
        };

        using var registry = NewRegistry();

        var (fresh, spec) = await registry.SelectAsync("bounding-box",
            new FeatureSelectionContext { Repository = repo, Parameters = parameters });
        fresh.Select(f => f.Id).Should().BeEquivalentTo(["inside"]);

        var (reloaded, _) = await registry.SelectAsync("bounding-box",
            new FeatureSelectionContext { Repository = repo, Parameters = RoundTrip(spec.Parameters) });
        reloaded.Select(f => f.Id).Should().BeEquivalentTo(["inside"]);
    }

    // ── nearby ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Nearby_SurvivesRoundTrip()
    {
        var repo = new TestAssetProvider();
        repo.Add(new GeoFeature { Id = "near", Geometry = new GeoPoint(0.1, 0.1) });
        repo.Add(new GeoFeature { Id = "far", Geometry = new GeoPoint(50, 50) });

        var parameters = new Dictionary<string, object>
        {
            ["center"] = new GeoPoint(0, 0),
            ["radiusDegrees"] = 1.0,
        };

        using var registry = NewRegistry();

        var (fresh, spec) = await registry.SelectAsync("nearby",
            new FeatureSelectionContext { Repository = repo, Parameters = parameters });
        fresh.Select(f => f.Id).Should().BeEquivalentTo(["near"]);

        var (reloaded, _) = await registry.SelectAsync("nearby",
            new FeatureSelectionContext { Repository = repo, Parameters = RoundTrip(spec.Parameters) });
        reloaded.Select(f => f.Id).Should().BeEquivalentTo(["near"]);
    }

    // ── asset-type-filter ────────────────────────────────────────────────────

    [Fact]
    public async Task AssetTypeFilter_SurvivesRoundTrip()
    {
        var repo = new TestAssetProvider();
        repo.Add(new GeoFeature { Id = "match", Properties = new GeoFeatureProperties { AssetTypeId = "hydrant" } });
        repo.Add(new GeoFeature { Id = "nomatch", Properties = new GeoFeatureProperties { AssetTypeId = "pole" } });

        var parameters = new Dictionary<string, object> { ["assetTypeId"] = "hydrant" };

        using var registry = NewRegistry();

        var (fresh, spec) = await registry.SelectAsync("asset-type-filter",
            new FeatureSelectionContext { Repository = repo, Parameters = parameters });
        fresh.Select(f => f.Id).Should().BeEquivalentTo(["match"]);

        var (reloaded, _) = await registry.SelectAsync("asset-type-filter",
            new FeatureSelectionContext { Repository = repo, Parameters = RoundTrip(spec.Parameters) });
        reloaded.Select(f => f.Id).Should().BeEquivalentTo(["match"]);
    }

    // ── topology-reachability ────────────────────────────────────────────────

    [Fact]
    public async Task TopologyReachability_SurvivesRoundTrip()
    {
        var repo = new TestAssetProvider();
        repo.Add(new GeoFeature
        {
            Id = "seed",
            Topology = [new TopoEdge { TargetId = "child" }],
        });
        repo.Add(new GeoFeature { Id = "child" });

        var parameters = new Dictionary<string, object>
        {
            ["featureId"] = "seed",
            ["direction"] = TraversalDirection.Downstream,
            ["includeSeed"] = true,
        };

        using var registry = NewRegistry();

        var (fresh, spec) = await registry.SelectAsync("topology-reachability",
            new FeatureSelectionContext { Repository = repo, Parameters = parameters });
        fresh.Select(f => f.Id).Should().BeEquivalentTo(["seed", "child"]);

        var (reloaded, _) = await registry.SelectAsync("topology-reachability",
            new FeatureSelectionContext { Repository = repo, Parameters = RoundTrip(spec.Parameters) });
        reloaded.Select(f => f.Id).Should().BeEquivalentTo(["seed", "child"]);
    }

    // ── manual ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Manual_SurvivesRoundTrip()
    {
        var repo = new TestAssetProvider();
        repo.Add(new GeoFeature { Id = "f1" });
        repo.Add(new GeoFeature { Id = "f2" });

        var parameters = new Dictionary<string, object> { ["featureIds"] = new List<string> { "f1", "f2" } };

        using var registry = NewRegistry();

        var (fresh, spec) = await registry.SelectAsync("manual",
            new FeatureSelectionContext { Repository = repo, Parameters = parameters });
        fresh.Select(f => f.Id).Should().BeEquivalentTo(["f1", "f2"]);

        var (reloaded, _) = await registry.SelectAsync("manual",
            new FeatureSelectionContext { Repository = repo, Parameters = RoundTrip(spec.Parameters) });
        reloaded.Select(f => f.Id).Should().BeEquivalentTo(["f1", "f2"]);
    }
}
