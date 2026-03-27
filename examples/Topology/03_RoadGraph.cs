// ────────────────────────────────────────────────────────────────────────────
// 03 · Road Graph
//
// Models a small urban road network where edges are bidirectional.
// Bidirectionality is expressed by adding two TopoEdge entries per road
// (one in each direction) — the standard approach for undirected graphs
// represented as directed graphs.
//
// Network layout (↔ = bidirectional road, weight = distance km):
//
//   Centro ─0.8─▶ Universidad ─1.2─▶ Hospital ─0.6─▶ Aeropuerto
//     └────2.0────────────────────────────────────────────┘
//   Centro ─1.5─▶ Zona Franca ─0.9─▶ Puerto
//
// Key concepts:
//   • HasCycles returns TRUE for road networks (loops are normal)
//   • FindShortestPath with distance weights gives the optimal GPS route
//   • TopologicalSort throws on cyclic graphs — use FindShortestPath instead
//   • GetConnectedComponents identifies disconnected road clusters
// ────────────────────────────────────────────────────────────────────────────
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Examples.Topology;

public static class RoadGraph
{
    public static void Run()
    {
        // ── 1. Create intersections as GeoPoint features ─────────────────────────

        var centro      = Node("Centro",      -69.90, 18.47);
        var universidad = Node("Universidad", -69.88, 18.49);
        var hospital    = Node("Hospital",    -69.86, 18.48);
        var aeropuerto  = Node("Aeropuerto",  -69.83, 18.48);
        var zonaFranca  = Node("Zona Franca", -69.88, 18.45);
        var puerto      = Node("Puerto",      -69.87, 18.43);

        // ── 2. Wire bidirectional roads (two directed edges per road) ─────────────

        Road(centro,      universidad, 0.8, speedKmh: 50);
        Road(universidad, hospital,    1.2, speedKmh: 40);
        Road(hospital,    aeropuerto,  0.6, speedKmh: 60);

        // Bypass: Centro → Aeropuerto directly
        Road(centro,      aeropuerto, 2.0, speedKmh: 80);

        // Secondary road to port
        Road(centro,     zonaFranca, 1.5, speedKmh: 60);
        Road(zonaFranca, puerto,     0.9, speedKmh: 40);

        // ── 3. Load into repository ──────────────────────────────────────────────

        var repo = new InMemoryAssetProvider();
        foreach (var f in new[] { centro, universidad, hospital, aeropuerto, zonaFranca, puerto })
            repo.Add(f);

        // ── 4. Road networks have cycles — validate detection ─────────────────────

        Print.Section("HasCycles → true es normal en redes viales (bucles de retorno)");
        Print.Bool("HasCycles", repo.HasCycles());   // true — bidirectional roads form cycles

        // ── 5. Shortest GPS route: Centro → Aeropuerto ───────────────────────────

        Print.Section("FindShortestPath (Centro → Aeropuerto) — ruta GPS mínima distancia");
        var route = repo.FindShortestPath(centro.Id, aeropuerto.Id);
        Print.Path(route);
        // Direct bypass (2.0 km) vs via university+hospital (0.8+1.2+0.6 = 2.6 km)
        Console.WriteLine("      → La ruta directa (bypass 2.0 km) es más corta que la vía universidad (2.6 km)");

        // ── 6. BFS detour — fewest intersections ─────────────────────────────────

        Print.Section("FindPath (Centro → Aeropuerto) — menos intersecciones (BFS)");
        Print.Path(repo.FindPath(centro.Id, aeropuerto.Id));
        // BFS finds the 1-hop direct route: Centro → Aeropuerto

        // ── 7. Direct neighbours of Centro ───────────────────────────────────────

        Print.Section("GetNeighbors (Centro) → intersecciones directamente conectadas");
        Print.List("Vecinos", repo.GetNeighbors(centro.Id));

        // ── 8. Connected components — detect isolated clusters ───────────────────

        Print.Section("GetConnectedComponents → clusters de red vial");
        Print.Components(repo.GetConnectedComponents());

        // ── 9. All nodes reachable from Centro ───────────────────────────────────

        Print.Section("GetDescendants (Centro) → toda la red alcanzable");
        Print.List("Alcanzable desde Centro", repo.GetDescendants(centro.Id));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GeoFeature Node(string name, double lon, double lat) =>
        new()
        {
            Properties = new() { Name = name, AssetTypeId = AssetType.Point.Id.ToString() },
            Geometry   = new GeoPoint { Coordinates = [lon, lat] }
        };

    /// <summary>Adds a pair of directed edges to model an undirected (bidirectional) road.</summary>
    private static void Road(GeoFeature a, GeoFeature b, double distanceKm, int speedKmh = 50)
    {
        var metadata = new Dictionary<string, string>
        {
            ["distanceKm"] = distanceKm.ToString("F1"),
            ["speedKmh"]   = speedKmh.ToString()
        };

        a.Topology.Add(new TopoEdge { TargetId = b.Id, Kind = "road", Weight = distanceKm, Metadata = metadata });
        b.Topology.Add(new TopoEdge { TargetId = a.Id, Kind = "road", Weight = distanceKm, Metadata = metadata });
    }
}
