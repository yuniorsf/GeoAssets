using System.Diagnostics;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

/// <summary>
/// Performance contract backing XD01-239's AC: feeder-trip traversal (<see cref="TopoGraph.GetDescendants"/>)
/// must resolve in under 30ms for a graph of at least 3,000 nodes.
/// </summary>
public class TopoGraphPerformanceTests
{
    /// <summary>
    /// Builds a branching feeder-like graph: a root switch with <paramref name="branchingFactor"/>
    /// downstream edges per node, breadth-first, until at least <paramref name="nodeCount"/> nodes exist.
    /// </summary>
    private static List<GeoFeature> BuildBranchingGraph(int nodeCount, int branchingFactor = 4)
    {
        var features = new List<GeoFeature> { new() { Id = "root" } };
        var frontier = new Queue<string>(["root"]);
        var next = 0;

        while (features.Count < nodeCount && frontier.TryDequeue(out var parentId))
        {
            var parent = features.First(f => string.Equals(f.Id, parentId, StringComparison.Ordinal));
            for (var i = 0; i < branchingFactor && features.Count < nodeCount; i++)
            {
                var childId = $"n{next++}";
                features.Add(new GeoFeature { Id = childId });
                parent.Topology.Add(new TopoEdge { TargetId = childId, Kind = "feeds" });
                frontier.Enqueue(childId);
            }
        }

        return features;
    }

    [Fact]
    public void GetDescendants_ThreeThousandNodeGraph_RespectsThirtyMillisecondContract()
    {
        var features = BuildBranchingGraph(nodeCount: 3_000);

        // Warm up (JIT) before timing, so the measured run reflects steady-state cost.
        _ = TopoGraph.GetDescendants("root", features, edgeKind: "feeds");

        var stopwatch = Stopwatch.StartNew();
        var descendants = TopoGraph.GetDescendants("root", features, edgeKind: "feeds");
        stopwatch.Stop();

        descendants.Should().HaveCount(features.Count - 1);
        stopwatch.Elapsed.TotalMilliseconds.Should().BeLessThan(30,
            "XD01-239's AC requires the traversal-driven order creation path to resolve downstream impact in under 30ms for ≥3,000-node graphs");
    }
}
