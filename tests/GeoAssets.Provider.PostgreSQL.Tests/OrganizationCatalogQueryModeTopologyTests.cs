using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using Xunit;
using static GeoAssets.Provider.PostgreSQL.Tests.QueryModeHarness;

namespace GeoAssets.Provider.PostgreSQL.Tests;

/// <summary>
/// The topology reads of a query-backed organization catalog (XD01-449): answered by SQL over the organization's and the
/// shared features, with <see cref="TopoGraph"/>'s semantics — the expected values are <see cref="TopoGraph"/> over the
/// very features seeded. Each test uses its own organizations, ids, asset types and map area.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OrganizationCatalogQueryModeTopologyTests(PostgresContainerFixture fixture)
{
    private const string Feeds = "feeds";
    private const string Serves = "serves";

    private readonly QueryModeHarness _harness = new(fixture);

    private sealed class Graph
    {
        public required QueryWorld World { get; init; }

        public required Dictionary<string, GeoFeature> Visible { get; init; }

        public required string[] ForeignIds { get; init; }

        public string Id(string name) => Visible[name].Id;
    }

    /// <summary>
    /// Own a, b, c, d, e; shared s; another organization's f. Edges: a feeds b, b (duplicated), s and f and a dangling
    /// id; b feeds c; c serves a (a cycle back to the start) and feeds d; s feeds e; e feeds a; d feeds f. a is a
    /// <c>pole</c>, b a <c>cable</c>, the rest <c>pole</c> or <c>cable</c> alternately.
    /// </summary>
    private async Task<Graph> SeedGraphAsync()
    {
        var world = NewWorld();
        string[] names = ["a", "b", "c", "d", "e", "s", "f"];
        var ids = names.ToDictionary(n => n, n => NewId(n), StringComparer.Ordinal);
        var dangling = NewId("dangling");
        var pole = $"po-{world.AssetType[2..20]}";
        var cable = $"ca-{world.AssetType[2..20]}";

        GeoFeature Make(string name, Guid organization, string type, params (string, string)[] edges) =>
            Feature(world, ids[name], organization, 0.001 * (Array.IndexOf(names, name) + 1), type, edges);

        GeoFeature[] features =
        [
            Make("a", world.Org, pole, (ids["b"], Feeds), (ids["b"], Feeds), (ids["s"], Feeds), (ids["f"], Feeds), (dangling, Feeds)),
            Make("b", world.Org, cable, (ids["c"], Feeds)),
            Make("c", world.Org, pole, (ids["a"], Serves), (ids["d"], Feeds)),
            Make("d", world.Org, cable, (ids["f"], Feeds)),
            Make("e", world.Org, pole, (ids["a"], Feeds)),
            Make("s", Guid.Empty, cable, (ids["e"], Feeds)),
            Make("f", world.Other, pole),
        ];
        await _harness.SeedAsync(features);
        return new Graph
        {
            World = world,
            Visible = features.Where(f => f.Properties.OrganizationId != world.Other).ToDictionary(f => Short(ids, f.Id), StringComparer.Ordinal),
            ForeignIds = [ids["f"]],
        };
    }

    private static string Short(Dictionary<string, string> ids, string id) => ids.Single(p => string.Equals(p.Value, id, StringComparison.Ordinal)).Key;

    private async Task<(QueryCatalogs Catalogs, GeoAssets.Core.Interfaces.IAssetProvider View)> QueryViewAsync(Graph graph, int limit = 1_000)
    {
        var catalogs = await _harness.NewCatalogsAsync(fullCacheMaxFeatures: 0, enumerationLimit: limit);
        await catalogs.Sut.WarmAsync(graph.World.Org, CancellationToken.None);
        catalogs.Sut.IsQueryBacked(graph.World.Org).Should().BeTrue();
        return (catalogs, catalogs.For(graph.World.Org));
    }

    [Fact]
    public async Task GetNeighbors_MatchesTopoGraph_KeepsEdgeOrderAndDuplicates_AndIgnoresForeignAndDanglingTargets()
    {
        var graph = await SeedGraphAsync();
        var (catalogs, view) = await QueryViewAsync(graph);
        await using var _ = catalogs;
        var visible = graph.Visible.Values.ToList();
        var a = graph.Id("a");

        Ids(view.GetNeighbors(a)).Should().Equal(Ids(TopoGraph.GetNeighbors(a, visible)));
        Ids(view.GetNeighbors(a)).Should().Equal(graph.Id("b"), graph.Id("b"), graph.Id("s"));
        Ids(view.GetNeighbors(a)).Should().NotContain(graph.ForeignIds);
        Ids(view.GetNeighbors(a, Feeds, $"ca-{graph.World.AssetType[2..20]}"))
            .Should().Equal(Ids(TopoGraph.GetNeighbors(a, visible, Feeds, $"ca-{graph.World.AssetType[2..20]}")));
        Ids(view.GetNeighbors(a, Feeds, $"po-{graph.World.AssetType[2..20]}")).Should().BeEmpty();
        Ids(view.GetNeighbors(graph.Id("c"), Serves)).Should().Equal(a);
        Ids(view.GetNeighbors(graph.Id("c"), "none")).Should().BeEmpty();
        Ids(view.GetNeighbors(graph.Id("d"))).Should().BeEmpty("its only target is another organization's");
        Ids(view.GetNeighbors(graph.Id("s"))).Should().Equal(new[] { graph.Id("e") }, "a shared feature's edges are traversed");
        Ids(view.GetNeighbors(graph.ForeignIds[0])).Should().BeEmpty();
        Ids(view.GetNeighbors(NewId("unknown"))).Should().BeEmpty();
    }

    [Fact]
    public async Task GetDescendants_MatchesTopoGraph_IncludesTheStartOnACycle_AndStopsAtTheEdgeKindFilter()
    {
        var graph = await SeedGraphAsync();
        var (catalogs, view) = await QueryViewAsync(graph);
        await using var _ = catalogs;
        var visible = graph.Visible.Values.ToList();
        var a = graph.Id("a");
        var cable = $"ca-{graph.World.AssetType[2..20]}";

        var all = Ids(view.GetDescendants(a)).ToList();
        all.Should().BeEquivalentTo(Ids(TopoGraph.GetDescendants(a, visible)));
        all.Should().Contain(a, "a - b - c - a closes a cycle").And.NotContain(graph.ForeignIds);
        all.Should().Equal(Ordinal(all), "query mode answers in ordinal id order");

        var feedsOnly = Ids(view.GetDescendants(a, Feeds)).ToList();
        feedsOnly.Should().BeEquivalentTo(Ids(TopoGraph.GetDescendants(a, visible, Feeds)));
        feedsOnly.Should().Equal(Ordinal(feedsOnly));
        feedsOnly.Should().Contain(a, "a feeds s, s feeds e and e feeds a: a is reached again");
        Ids(view.GetDescendants(graph.Id("c"), Feeds)).Should().Equal(new[] { graph.Id("d") }, "the serves edge back to a is not walked");
        Ids(view.GetDescendants(graph.Id("c"), Feeds)).Should().BeEquivalentTo(Ids(TopoGraph.GetDescendants(graph.Id("c"), visible, Feeds)));

        Ids(view.GetDescendants(a, Serves)).Should().BeEmpty("a has no outgoing serves edge");
        Ids(view.GetDescendants(graph.Id("c"), Serves)).Should().BeEquivalentTo(Ids(TopoGraph.GetDescendants(graph.Id("c"), visible, Serves)));
        Ids(view.GetDescendants(a, null, cable)).Should().BeEquivalentTo(Ids(TopoGraph.GetDescendants(a, visible, null, cable)));
        Ids(view.GetDescendants(a, Feeds, cable)).Should().BeEquivalentTo(Ids(TopoGraph.GetDescendants(a, visible, Feeds, cable)));
        Ids(view.GetDescendants(graph.Id("s"))).Should().BeEquivalentTo(Ids(TopoGraph.GetDescendants(graph.Id("s"), visible)), "a walk may start at, and pass through, a shared feature");
        Ids(view.GetDescendants(NewId("unknown"))).Should().BeEmpty();
        Ids(view.GetDescendants(graph.ForeignIds[0])).Should().BeEmpty();
    }

    [Fact]
    public async Task GetAncestors_MatchesTopoGraph_ThroughSharedFeatures_NeverReturningAnotherOrganizationsFeature()
    {
        var graph = await SeedGraphAsync();
        var (catalogs, view) = await QueryViewAsync(graph);
        await using var _ = catalogs;
        var visible = graph.Visible.Values.ToList();

        foreach (var name in new[] { "a", "b", "c", "d", "e", "s" })
        {
            var id = graph.Id(name);
            var ancestors = Ids(view.GetAncestors(id)).ToList();
            ancestors.Should().BeEquivalentTo(Ids(TopoGraph.GetAncestors(id, visible)), name);
            ancestors.Should().Equal(Ordinal(ancestors), "query mode answers in ordinal id order");
            ancestors.Should().NotContain(graph.ForeignIds);
        }

        Ids(view.GetAncestors(graph.Id("e"))).Should().Contain(graph.Id("s"));
        Ids(view.GetAncestors(graph.Id("a"))).Should().Contain(graph.Id("a"), "a is on a cycle");
        Ids(view.GetAncestors(NewId("unknown"))).Should().BeEmpty();
        Ids(view.GetAncestors(graph.ForeignIds[0])).Should().BeEmpty("another organization's feature is not a start");
    }

    [Fact]
    public async Task GetDescendants_BeyondTheEnumerationLimit_IsRefused_AndAtItServed()
    {
        var graph = await SeedGraphAsync();
        var a = graph.Id("a");
        var reached = TopoGraph.GetDescendants(a, graph.Visible.Values.ToList()).Count;
        var (refusedCatalogs, refused) = await QueryViewAsync(graph, limit: reached - 1);
        await using var _ = refusedCatalogs;
        var (servedCatalogs, served) = await QueryViewAsync(graph, limit: reached);
        await using var __ = servedCatalogs;

        var descendants = () => refused.GetDescendants(a);

        descendants.Should().Throw<CatalogEnumerationLimitException>().Which.Should().Match<CatalogEnumerationLimitException>(
            e => e.Operation == "GetDescendants" && e.Limit == reached - 1);
        served.GetDescendants(a).Should().HaveCount(reached);
    }
}
