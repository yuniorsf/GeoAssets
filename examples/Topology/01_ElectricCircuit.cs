// ────────────────────────────────────────────────────────────────────────────
// 01 · Electric Circuit
//
// Models a simple radial power distribution network and runs topology queries
// that are typical in load-flow analysis and fault-path tracing.
//
// Network layout (→ = directed edge, weight = Ω resistance):
//
//   Generator ──0.10Ω──▶ Transformer ──0.05Ω──▶ Cable A ──0.30Ω──▶ Junction ──0.20Ω──▶ Load: Factory
//                                                                        └─────────────────0.40Ω──▶ Load: Hospital
//
// Key concepts:
//   • TopoEdge.Kind    = "electric-flow"   (domain label)
//   • TopoEdge.Weight  = resistance in Ω   (Dijkstra minimises total resistance)
//   • Metadata stores engineering attributes (voltage, current)
// ────────────────────────────────────────────────────────────────────────────
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Examples.Topology;

public static class ElectricCircuit
{
    public static void Run()
    {
        // ── 1. Create nodes (GeoFeatures as geographic network elements) ─────────

        var generator    = Node("Subestación Generadora",  -69.90, 18.50);
        var transformer  = Node("Transformador Principal", -69.88, 18.51);
        var cableA       = Node("Cable A (230kV)",         -69.86, 18.52);
        var junction     = Node("Nodo de Distribución",   -69.84, 18.52);
        var loadFactory  = Node("Carga: Fábrica",         -69.82, 18.54);
        var loadHospital = Node("Carga: Hospital",        -69.82, 18.50);

        // ── 2. Wire directed edges (resistance as weight) ────────────────────────

        //                kind              weight   metadata
        Connect(generator,   transformer,  "electric-flow", 0.10, voltage: "230kV", current: "500A");
        Connect(transformer, cableA,       "electric-flow", 0.05, voltage: "115kV", current: "900A");
        Connect(cableA,      junction,     "electric-flow", 0.30, voltage: "115kV", current: "900A");
        Connect(junction,    loadFactory,  "electric-flow", 0.20, voltage: "13.8kV", current: "300A");
        Connect(junction,    loadHospital, "electric-flow", 0.40, voltage: "13.8kV", current: "250A");

        // ── 3. Load into repository ──────────────────────────────────────────────

        var repo = new InMemoryAssetProvider();
        foreach (var f in new[] { generator, transformer, cableA, junction, loadFactory, loadHospital })
            repo.Add(f);

        // ── 4. Validate: is this a radial (tree) network? ────────────────────────

        Print.Section("Validación: ¿red radial (sin ciclos)?");
        Print.Bool("HasCycles", repo.HasCycles());   // false → radial ✓

        // ── 5. Topological sort → correct load-flow calculation order ────────────

        Print.Section("Topological sort (orden de cálculo de flujo de carga)");
        var order = repo.TopologicalSort();
        foreach (var (f, i) in order.Select((f, i) => (f, i + 1)))
            Console.WriteLine($"      {i}. {f.Properties.Name}");

        // ── 6. All nodes fed by the generator ────────────────────────────────────

        Print.Section("GetDescendants (generator) → todos los nodos aguas abajo");
        Print.List("Aguas abajo", repo.GetDescendants(generator.Id));

        // ── 7. Direct loads at the junction ──────────────────────────────────────

        Print.Section("GetNeighbors (junction) → cargas directas");
        Print.List("Vecinos directos", repo.GetNeighbors(junction.Id));

        // ── 8. Minimum-resistance path to the hospital (Dijkstra) ───────────────

        Print.Section("FindShortestPath (generator → hospital) — mínima resistencia total");
        var path = repo.FindShortestPath(generator.Id, loadHospital.Id);
        Print.Path(path);
        var totalR = 0.10 + 0.05 + 0.30 + 0.40;
        Console.WriteLine($"      Resistencia total: {totalR} Ω");

        // ── 9. Fault tracing: upstream sources of the hospital ───────────────────

        Print.Section("GetAncestors (hospital) → fuentes aguas arriba (traza de falla)");
        Print.List("Aguas arriba", repo.GetAncestors(loadHospital.Id));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GeoFeature Node(string name, double lon, double lat) =>
        new()
        {
            Properties = new() { Name = name, AssetTypeId = AssetType.Point.Id.ToString() },
            Geometry   = new GeoPoint { Coordinates = [lon, lat] }
        };

    private static void Connect(
        GeoFeature from, GeoFeature to,
        string kind, double resistance,
        string voltage = "", string current = "")
    {
        from.Topology.Add(new TopoEdge
        {
            TargetId = to.Id,
            Kind     = kind,
            Weight   = resistance,
            Metadata = new() { ["voltage"] = voltage, ["current"] = current }
        });
    }
}
