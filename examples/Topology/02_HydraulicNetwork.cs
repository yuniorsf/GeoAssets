// ────────────────────────────────────────────────────────────────────────────
// 02 · Hydraulic Network
//
// Models a municipal water distribution system with two isolated sectors.
// Shows how topological queries answer operational questions in SCADA/GIS:
//   – Which consumers lose water if a pump fails?
//   – What is the shortest pipe route between two points?
//   – Are there isolated network segments without supply?
//
// Network layout (→ = directed flow, weight = pipe friction loss in mWC):
//
//  Sector A (connected)
//    Embalse ──3──▶ Bomba ──2──▶ Colector ──4──▶ Barrio Norte
//                                   └──────6──▶ Barrio Sur
//
//  Sector B (isolated — simulates a network segment not yet connected)
//    Cisterna ──1──▶ Tanque Elevado ──2──▶ Zona Industrial
//
// Key concepts:
//   • TopoEdge.Weight  = friction loss in metres of water column (mWC)
//   • TopoEdge.Kind    = "water-flow"
//   • GetConnectedComponents detects isolated sectors automatically
// ────────────────────────────────────────────────────────────────────────────
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Examples.Topology;

public static class HydraulicNetwork
{
    public static void Run()
    {
        // ── 1. Sector A — main distribution network ──────────────────────────────

        var embalse      = Node("Embalse Principal",  -69.91, 18.53);
        var bomba        = Node("Estación de Bombeo", -69.89, 18.53);
        var colector     = Node("Colector Central",   -69.87, 18.53);
        var barrioNorte  = Node("Barrio Norte",       -69.85, 18.55);
        var barrioSur    = Node("Barrio Sur",         -69.85, 18.51);

        Connect(embalse,  bomba,       3.0, diameter: "DN600", material: "HDPE");
        Connect(bomba,    colector,    2.0, diameter: "DN400", material: "HDPE");
        Connect(colector, barrioNorte, 4.0, diameter: "DN200", material: "PVC");
        Connect(colector, barrioSur,   6.0, diameter: "DN150", material: "PVC");

        // ── 2. Sector B — isolated network (not yet connected to main supply) ────

        var cisterna       = Node("Cisterna de Emergencia", -69.93, 18.48);
        var tanqueElevado  = Node("Tanque Elevado",         -69.91, 18.48);
        var zonaIndustrial = Node("Zona Industrial",        -69.89, 18.48);

        Connect(cisterna,      tanqueElevado,  1.0, diameter: "DN300", material: "Acero");
        Connect(tanqueElevado, zonaIndustrial, 2.0, diameter: "DN200", material: "Acero");

        // ── 3. Load into repository ──────────────────────────────────────────────

        var repo = new InMemoryAssetProvider();
        foreach (var f in new[] { embalse, bomba, colector, barrioNorte, barrioSur,
                                   cisterna, tanqueElevado, zonaIndustrial })
            repo.Add(f);

        // ── 4. Detect isolated network segments ──────────────────────────────────

        Print.Section("GetConnectedComponents → segmentos de red aislados");
        Print.Components(repo.GetConnectedComponents());
        // → 2 components: Sector A and Sector B

        // ── 5. Simulate pump failure: which consumers are affected? ───────────────

        Print.Section("GetDescendants (bomba) → consumidores afectados si la bomba falla");
        Print.List("Afectados", repo.GetDescendants(bomba.Id));

        // ── 6. BFS route — fewest pipe sections from embalse to Barrio Sur ───────

        Print.Section("FindPath (embalse → Barrio Sur) — ruta con menos tramos");
        Print.Path(repo.FindPath(embalse.Id, barrioSur.Id));

        // ── 7. Minimum friction-loss path (Dijkstra) ─────────────────────────────

        Print.Section("FindShortestPath (embalse → Barrio Norte) — mínima pérdida de carga");
        var path  = repo.FindShortestPath(embalse.Id, barrioNorte.Id);
        Print.Path(path);
        var totalLoss = 3.0 + 2.0 + 4.0;
        Console.WriteLine($"      Pérdida de carga total: {totalLoss} mWC");

        // ── 8. Trace upstream sources for Barrio Sur ─────────────────────────────

        Print.Section("GetAncestors (Barrio Sur) → fuentes de suministro aguas arriba");
        Print.List("Aguas arriba", repo.GetAncestors(barrioSur.Id));

        // ── 9. Validate: no cycles (radial distribution, no loop feed) ────────────

        Print.Section("Validación: ¿red radial sin ciclos?");
        Print.Bool("HasCycles", repo.HasCycles());
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GeoFeature Node(string name, double lon, double lat) =>
        new()
        {
            Properties = new() { Name = name, AssetTypeId = AssetType.Point.Id.ToString() },
            Geometry   = new GeoPoint { Coordinates = [lon, lat] }
        };

    private static void Connect(
        GeoFeature from, GeoFeature to, double frictionLossMwc,
        string diameter = "", string material = "")
    {
        from.Topology.Add(new TopoEdge
        {
            TargetId = to.Id,
            Kind     = "water-flow",
            Weight   = frictionLossMwc,
            Metadata = new() { ["diameter"] = diameter, ["material"] = material }
        });
    }
}
