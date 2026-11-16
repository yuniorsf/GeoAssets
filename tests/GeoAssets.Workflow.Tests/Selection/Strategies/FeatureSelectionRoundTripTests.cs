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
    private static Dictionary<string, object> RoundTrip(IReadOnlyDictionary<string, object> parameters)
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

        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
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

        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
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

        var parameters = new Dictionary<string, object>(StringComparer.Ordinal) { ["assetTypeId"] = "hydrant" };

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

        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
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

        var parameters = new Dictionary<string, object>(StringComparer.Ordinal) { ["featureIds"] = new List<string> { "f1", "f2" } };

        using var registry = NewRegistry();

        var (fresh, spec) = await registry.SelectAsync("manual",
            new FeatureSelectionContext { Repository = repo, Parameters = parameters });
        fresh.Select(f => f.Id).Should().BeEquivalentTo(["f1", "f2"]);

        var (reloaded, _) = await registry.SelectAsync("manual",
            new FeatureSelectionContext { Repository = repo, Parameters = RoundTrip(spec.Parameters) });
        reloaded.Select(f => f.Id).Should().BeEquivalentTo(["f1", "f2"]);
    }

    // ── polygon-fence ────────────────────────────────────────────────────────

    [Fact]
    public async Task PolygonFence_SurvivesRoundTrip()
    {
        var repo = new TestAssetProvider();
        repo.Add(new GeoFeature { Id = "inside", Geometry = new GeoPoint(5, 5), Properties = new GeoFeatureProperties { AssetTypeId = "pole" } });
        repo.Add(new GeoFeature { Id = "on-edge", Geometry = new GeoPoint(10, 5), Properties = new GeoFeatureProperties { AssetTypeId = "pole" } });
        repo.Add(new GeoFeature { Id = "other-type", Geometry = new GeoPoint(6, 6), Properties = new GeoFeatureProperties { AssetTypeId = "box" } });
        repo.Add(new GeoFeature { Id = "outside", Geometry = new GeoPoint(50, 50), Properties = new GeoFeatureProperties { AssetTypeId = "pole" } });
        repo.Add(new GeoFeature { Id = "zz-cut", Geometry = new GeoPoint(7, 7), Properties = new GeoFeatureProperties { AssetTypeId = "pole" } });

        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["rings"] = new double[][][] { [[0, 0], [10, 0], [10, 10], [0, 10], [0, 0]] },
            ["assetTypeIds"] = new List<string> { "pole" },
            ["maxResults"] = 2,
        };

        using var registry = NewRegistry();

        var (fresh, spec) = await registry.SelectAsync("polygon-fence",
            new FeatureSelectionContext { Repository = repo, Parameters = parameters });
        fresh.Select(f => f.Id).Should().Equal("inside", "on-edge");

        var reloadedParameters = RoundTrip(spec.Parameters);
        reloadedParameters.Values.Should().AllBeOfType<JsonElement>();

        var (reloaded, _) = await registry.SelectAsync("polygon-fence",
            new FeatureSelectionContext { Repository = repo, Parameters = reloadedParameters });
        reloaded.Select(f => f.Id).Should().Equal("inside", "on-edge");
    }

    // ── identifier-list ──────────────────────────────────────────────────────

    [Fact]
    public async Task IdentifierList_SurvivesRoundTrip()
    {
        static GeoFeature Plated(string id, string plate, string assetTypeId) => new()
        {
            Id = id,
            Properties = new GeoFeatureProperties
            {
                AssetTypeId = assetTypeId,
                CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["plate"] = plate },
            },
        };

        var repo = new TestAssetProvider();
        repo.Add(Plated("p-1", "100", "pole"));
        repo.Add(Plated("p-2", "200", "pole"));
        repo.Add(Plated("b-1", "100", "box"));
        repo.Add(Plated("p-3", "300", "pole"));

        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["attributeKey"] = "plate",
            ["values"] = new List<string> { " 100 ", "200", "200", "" },
            ["assetTypeIds"] = new[] { "pole" },
        };

        using var registry = NewRegistry();

        var (fresh, spec) = await registry.SelectAsync("identifier-list",
            new FeatureSelectionContext { Repository = repo, Parameters = parameters });
        fresh.Select(f => f.Id).Should().Equal("p-1", "p-2");

        var reloadedParameters = RoundTrip(spec.Parameters);
        reloadedParameters.Values.Should().AllBeOfType<JsonElement>();

        var (reloaded, _) = await registry.SelectAsync("identifier-list",
            new FeatureSelectionContext { Repository = repo, Parameters = reloadedParameters });
        reloaded.Select(f => f.Id).Should().Equal("p-1", "p-2");
    }

    [Fact]
    public async Task IdentifierList_ReservedIdKey_SurvivesRoundTrip()
    {
        var repo = new TestAssetProvider();
        repo.Add(new GeoFeature { Id = "f1" });
        repo.Add(new GeoFeature { Id = "f2" });
        repo.Add(new GeoFeature { Id = "f3" });

        var parameters = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["attributeKey"] = "id",
            ["values"] = new[] { "f3", "f1" },
        };

        using var registry = NewRegistry();

        var (fresh, spec) = await registry.SelectAsync("identifier-list",
            new FeatureSelectionContext { Repository = repo, Parameters = parameters });
        fresh.Select(f => f.Id).Should().Equal("f1", "f3");

        var (reloaded, _) = await registry.SelectAsync("identifier-list",
            new FeatureSelectionContext { Repository = repo, Parameters = RoundTrip(spec.Parameters) });
        reloaded.Select(f => f.Id).Should().Equal("f1", "f3");
    }
}
