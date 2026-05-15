using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Xunit;

namespace GeoAssets.Core.Tests.Services;

public class TopoGraphTests
{
    private static GeoFeature F(string id, params string[] targets) => new()
    {
        Id = id,
        Topology = [.. targets.Select(t => new TopoEdge { TargetId = t })]
    };

    private static GeoFeature Fw(string id, params (string t, double w)[] edges) => new()
    {
        Id = id,
        Topology = [.. edges.Select(e => new TopoEdge { TargetId = e.t, Weight = e.w })]
    };

    // ── GetNeighbors ──────────────────────────────────────────────────────────

    [Fact]
    public void GetNeighbors_UnknownId_ReturnsEmpty()
    {
        TopoGraph.GetNeighbors("x", [F("a")]).Should().BeEmpty();
    }

    [Fact]
    public void GetNeighbors_NoTopology_ReturnsEmpty()
    {
        TopoGraph.GetNeighbors("a", [F("a")]).Should().BeEmpty();
    }

    [Fact]
    public void GetNeighbors_AllDanglingEdges_ReturnsEmpty()
    {
        TopoGraph.GetNeighbors("a", [F("a", "x", "y")]).Should().BeEmpty();
    }

    [Fact]
    public void GetNeighbors_MixedDanglingAndValid_ReturnsOnlyKnown()
    {
        var result = TopoGraph.GetNeighbors("a", [F("a", "b", "x"), F("b")]);
        result.Should().ContainSingle().Which.Id.Should().Be("b");
    }

    [Fact]
    public void GetNeighbors_AllEdgesValid_ReturnsAllNeighbors()
    {
        var result = TopoGraph.GetNeighbors("a", [F("a", "b", "c"), F("b"), F("c")]);
        result.Select(f => f.Id).Should().BeEquivalentTo(["b", "c"]);
    }

    // ── GetDescendants ────────────────────────────────────────────────────────

    [Fact]
    public void GetDescendants_EmptyCollection_ReturnsEmpty()
    {
        TopoGraph.GetDescendants("a", []).Should().BeEmpty();
    }

    [Fact]
    public void GetDescendants_UnknownId_ReturnsEmpty()
    {
        TopoGraph.GetDescendants("x", [F("a")]).Should().BeEmpty();
    }

    [Fact]
    public void GetDescendants_IsolatedNode_ReturnsEmpty()
    {
        TopoGraph.GetDescendants("a", [F("a"), F("b")]).Should().BeEmpty();
    }

    [Fact]
    public void GetDescendants_DirectChildren_ReturnsNeighbors()
    {
        var result = TopoGraph.GetDescendants("a", [F("a", "b", "c"), F("b"), F("c")]);
        result.Select(f => f.Id).Should().BeEquivalentTo(["b", "c"]);
    }

    [Fact]
    public void GetDescendants_MultiLevelChain_ReturnsAllReachable()
    {
        var result = TopoGraph.GetDescendants("a", [F("a", "b"), F("b", "c"), F("c")]);
        result.Select(f => f.Id).Should().BeEquivalentTo(["b", "c"]);
    }

    [Fact]
    public void GetDescendants_StartNodeExcluded()
    {
        var result = TopoGraph.GetDescendants("a", [F("a", "b"), F("b")]);
        result.Should().NotContain(f => f.Id == "a");
    }

    [Fact]
    public void GetDescendants_CycleInGraph_TerminatesAndReturnsVisited()
    {
        // a→b→c→b  — BFS must not loop infinitely
        var result = TopoGraph.GetDescendants("a", [F("a", "b"), F("b", "c"), F("c", "b")]);
        result.Select(f => f.Id).Should().BeEquivalentTo(["b", "c"]);
    }

    // ── GetAncestors ──────────────────────────────────────────────────────────

    [Fact]
    public void GetAncestors_UnknownId_ReturnsEmpty()
    {
        TopoGraph.GetAncestors("x", [F("a")]).Should().BeEmpty();
    }

    [Fact]
    public void GetAncestors_NoIncomingEdges_ReturnsEmpty()
    {
        TopoGraph.GetAncestors("a", [F("a", "b"), F("b")]).Should().BeEmpty();
    }

    [Fact]
    public void GetAncestors_DirectAncestor_ReturnsIt()
    {
        var result = TopoGraph.GetAncestors("b", [F("a", "b"), F("b")]);
        result.Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public void GetAncestors_MultiLevel_ReturnsAllAncestors()
    {
        var result = TopoGraph.GetAncestors("c", [F("a", "b"), F("b", "c"), F("c")]);
        result.Select(f => f.Id).Should().BeEquivalentTo(["a", "b"]);
    }

    [Fact]
    public void GetAncestors_StartNodeExcluded()
    {
        var result = TopoGraph.GetAncestors("b", [F("a", "b"), F("b")]);
        result.Should().NotContain(f => f.Id == "b");
    }

    [Fact]
    public void GetAncestors_CycleInGraph_TerminatesAndReturnsVisited()
    {
        // a→b→c→a  — reverse BFS from c must not loop
        var result = TopoGraph.GetAncestors("c", [F("a", "b"), F("b", "c"), F("c", "a")]);
        result.Select(f => f.Id).Should().BeEquivalentTo(["a", "b"]);
    }

    // ── TopologicalSort ───────────────────────────────────────────────────────

    [Fact]
    public void TopologicalSort_EmptyCollection_ReturnsEmpty()
    {
        TopoGraph.TopologicalSort([]).Should().BeEmpty();
    }

    [Fact]
    public void TopologicalSort_SingleNode_ReturnsThatNode()
    {
        TopoGraph.TopologicalSort([F("a")]).Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public void TopologicalSort_LinearChain_SourceBeforeDescendants()
    {
        var result = TopoGraph.TopologicalSort([F("b", "c"), F("a", "b"), F("c")]);
        var ids = result.Select(f => f.Id).ToList();
        ids.IndexOf("a").Should().BeLessThan(ids.IndexOf("b"));
        ids.IndexOf("b").Should().BeLessThan(ids.IndexOf("c"));
    }

    [Fact]
    public void TopologicalSort_DiamondGraph_SourcesBeforeSharedSink()
    {
        // a→c, b→c — both a and b must appear before c
        var result = TopoGraph.TopologicalSort([F("a", "c"), F("b", "c"), F("c")]);
        var ids = result.Select(f => f.Id).ToList();
        ids.IndexOf("a").Should().BeLessThan(ids.IndexOf("c"));
        ids.IndexOf("b").Should().BeLessThan(ids.IndexOf("c"));
    }

    [Fact]
    public void TopologicalSort_IsolatedNodesIncluded()
    {
        var result = TopoGraph.TopologicalSort([F("a", "b"), F("b"), F("iso")]);
        result.Select(f => f.Id).Should().BeEquivalentTo(["a", "b", "iso"]);
    }

    [Fact]
    public void TopologicalSort_SelfLoop_ThrowsInvalidOperationException()
    {
        var act = () => TopoGraph.TopologicalSort([F("a", "a")]);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TopologicalSort_SimpleCycle_ThrowsInvalidOperationException()
    {
        var act = () => TopoGraph.TopologicalSort([F("a", "b"), F("b", "a")]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*cycle*");
    }

    [Fact]
    public void TopologicalSort_AllNodesPresent()
    {
        var result = TopoGraph.TopologicalSort([F("a", "b"), F("b", "c"), F("c")]);
        result.Should().HaveCount(3);
    }

    // ── FindPath ──────────────────────────────────────────────────────────────

    [Fact]
    public void FindPath_EmptyCollection_ReturnsEmpty()
    {
        TopoGraph.FindPath("a", "b", []).Should().BeEmpty();
    }

    [Fact]
    public void FindPath_UnknownFromId_ReturnsEmpty()
    {
        TopoGraph.FindPath("x", "a", [F("a")]).Should().BeEmpty();
    }

    [Fact]
    public void FindPath_UnknownToId_ReturnsEmpty()
    {
        TopoGraph.FindPath("a", "x", [F("a")]).Should().BeEmpty();
    }

    [Fact]
    public void FindPath_SameFromAndTo_ReturnsSingleNode()
    {
        var result = TopoGraph.FindPath("a", "a", [F("a")]);
        result.Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public void FindPath_DirectEdge_ReturnsTwoNodePath()
    {
        var result = TopoGraph.FindPath("a", "b", [F("a", "b"), F("b")]);
        result.Select(f => f.Id).Should().Equal("a", "b");
    }

    [Fact]
    public void FindPath_MultiHop_ReturnsFullOrderedPath()
    {
        var result = TopoGraph.FindPath("a", "c", [F("a", "b"), F("b", "c"), F("c")]);
        result.Select(f => f.Id).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void FindPath_WrongDirection_ReturnsEmpty()
    {
        // only edge is b→a; asking a→b should return nothing
        var result = TopoGraph.FindPath("a", "b", [F("a"), F("b", "a")]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindPath_MultiplePathsExist_ReturnsFewestHops()
    {
        // a→d (1 hop) beats a→b→d and a→c→d (2 hops)
        var features = new[] { F("a", "b", "c", "d"), F("b", "d"), F("c", "d"), F("d") };
        var result = TopoGraph.FindPath("a", "d", features);
        result.Select(f => f.Id).Should().Equal("a", "d");
    }

    // ── FindShortestPath ──────────────────────────────────────────────────────

    [Fact]
    public void FindShortestPath_EmptyCollection_ReturnsEmpty()
    {
        TopoGraph.FindShortestPath("a", "b", []).Should().BeEmpty();
    }

    [Fact]
    public void FindShortestPath_UnknownFromId_ReturnsEmpty()
    {
        TopoGraph.FindShortestPath("x", "a", [F("a")]).Should().BeEmpty();
    }

    [Fact]
    public void FindShortestPath_UnknownToId_ReturnsEmpty()
    {
        TopoGraph.FindShortestPath("a", "x", [F("a")]).Should().BeEmpty();
    }

    [Fact]
    public void FindShortestPath_SameFromAndTo_ReturnsSingleNode()
    {
        var result = TopoGraph.FindShortestPath("a", "a", [F("a")]);
        result.Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public void FindShortestPath_DirectEdge_ReturnsTwoNodePath()
    {
        var result = TopoGraph.FindShortestPath("a", "b", [Fw("a", ("b", 5.0)), F("b")]);
        result.Select(f => f.Id).Should().Equal("a", "b");
    }

    [Fact]
    public void FindShortestPath_MultiHop_ReturnsFullOrderedPath()
    {
        var result = TopoGraph.FindShortestPath("a", "c",
            [Fw("a", ("b", 1.0)), Fw("b", ("c", 1.0)), F("c")]);
        result.Select(f => f.Id).Should().Equal("a", "b", "c");
    }

    [Fact]
    public void FindShortestPath_WrongDirection_ReturnsEmpty()
    {
        var result = TopoGraph.FindShortestPath("a", "b", [F("a"), Fw("b", ("a", 1.0))]);
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindShortestPath_TwoPaths_ReturnsLowerWeightPath()
    {
        // a→b(10)  vs  a→c(1)→b(1)  — Dijkstra must pick a→c→b (cost 2)
        var features = new[]
        {
            new GeoFeature { Id = "a", Topology =
            [
                new TopoEdge { TargetId = "b", Weight = 10.0 },
                new TopoEdge { TargetId = "c", Weight = 1.0 }
            ]},
            Fw("c", ("b", 1.0)),
            F("b")
        };
        var result = TopoGraph.FindShortestPath("a", "b", features);
        result.Select(f => f.Id).Should().Equal("a", "c", "b");
    }

    [Fact]
    public void FindShortestPath_StaleQueueEntry_SkippedAndCorrectResult()
    {
        // a→b(10), a→c(1), c→b(1), b→d(20)
        // After c relaxes b, stale (b,10) sits in the PQ ahead of (d,22).
        // It must be dequeued and discarded (priority > dist[b]) before d is reached.
        var features = new[]
        {
            new GeoFeature { Id = "a", Topology =
            [
                new TopoEdge { TargetId = "b", Weight = 10.0 },
                new TopoEdge { TargetId = "c", Weight = 1.0 }
            ]},
            Fw("c", ("b", 1.0)),
            Fw("b", ("d", 20.0)),
            F("d")
        };
        var result = TopoGraph.FindShortestPath("a", "d", features);
        result.Select(f => f.Id).Should().Equal("a", "c", "b", "d");
    }

    [Fact]
    public void FindShortestPath_DanglingEdge_IsSkipped()
    {
        // "x" is not in the feature list — edge a→x must not throw
        var result = TopoGraph.FindShortestPath("a", "b",
            [Fw("a", ("b", 1.0), ("x", 1.0)), F("b")]);
        result.Select(f => f.Id).Should().Equal("a", "b");
    }

    // ── GetConnectedComponents ────────────────────────────────────────────────

    [Fact]
    public void GetConnectedComponents_EmptyCollection_ReturnsEmpty()
    {
        TopoGraph.GetConnectedComponents([]).Should().BeEmpty();
    }

    [Fact]
    public void GetConnectedComponents_SingleNode_ReturnsOneComponent()
    {
        var result = TopoGraph.GetConnectedComponents([F("a")]);
        result.Should().ContainSingle();
        result[0].Should().ContainSingle().Which.Id.Should().Be("a");
    }

    [Fact]
    public void GetConnectedComponents_AllConnected_ReturnsSingleComponent()
    {
        var result = TopoGraph.GetConnectedComponents([F("a", "b"), F("b", "c"), F("c")]);
        result.Should().ContainSingle();
        result[0].Select(f => f.Id).Should().BeEquivalentTo(["a", "b", "c"]);
    }

    [Fact]
    public void GetConnectedComponents_TwoDisconnectedGroups_ReturnsTwoComponents()
    {
        var result = TopoGraph.GetConnectedComponents(
            [F("a", "b"), F("b"), F("c", "d"), F("d")]);
        result.Should().HaveCount(2);
        result.SelectMany(c => c.Select(f => f.Id))
            .Should().BeEquivalentTo(["a", "b", "c", "d"]);
    }

    [Fact]
    public void GetConnectedComponents_DanglingEdge_DoesNotConnectExtraNodes()
    {
        // a→x where x is unknown — a and b must remain separate components
        var result = TopoGraph.GetConnectedComponents([F("a", "x"), F("b")]);
        result.Should().HaveCount(2);
    }

    [Fact]
    public void GetConnectedComponents_DirectedEdgeTreatedAsUndirected()
    {
        // a→b counts as an undirected link between a and b
        var result = TopoGraph.GetConnectedComponents([F("a", "b"), F("b")]);
        result.Should().ContainSingle();
        result[0].Select(f => f.Id).Should().BeEquivalentTo(["a", "b"]);
    }

    // ── HasCycles ─────────────────────────────────────────────────────────────

    [Fact]
    public void HasCycles_EmptyCollection_ReturnsFalse()
    {
        TopoGraph.HasCycles([]).Should().BeFalse();
    }

    [Fact]
    public void HasCycles_SingleNodeNoEdges_ReturnsFalse()
    {
        TopoGraph.HasCycles([F("a")]).Should().BeFalse();
    }

    [Fact]
    public void HasCycles_LinearChain_ReturnsFalse()
    {
        TopoGraph.HasCycles([F("a", "b"), F("b", "c"), F("c")]).Should().BeFalse();
    }

    [Fact]
    public void HasCycles_DagWithConvergingPaths_ReturnsFalse()
    {
        // a→b, a→c, b→c — c is black when a tries to visit it again; must not be a false positive
        TopoGraph.HasCycles([F("a", "b", "c"), F("b", "c"), F("c")]).Should().BeFalse();
    }

    [Fact]
    public void HasCycles_SelfLoop_ReturnsTrue()
    {
        TopoGraph.HasCycles([F("a", "a")]).Should().BeTrue();
    }

    [Fact]
    public void HasCycles_SimpleCycleTwoNodes_ReturnsTrue()
    {
        TopoGraph.HasCycles([F("a", "b"), F("b", "a")]).Should().BeTrue();
    }

    [Fact]
    public void HasCycles_LargerCycle_ReturnsTrue()
    {
        TopoGraph.HasCycles([F("a", "b"), F("b", "c"), F("c", "a")]).Should().BeTrue();
    }

    [Fact]
    public void HasCycles_OneComponentCyclicOtherAcyclic_ReturnsTrue()
    {
        // {a→b} is acyclic; {c→d→c} is cyclic — overall must return true
        TopoGraph.HasCycles([F("a", "b"), F("b"), F("c", "d"), F("d", "c")]).Should().BeTrue();
    }
}
