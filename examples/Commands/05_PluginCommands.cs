// ────────────────────────────────────────────────────────────────────────────
// 05 · MEF Plugin Commands
//
// Demonstrates the Command + Plugin pattern using MEF (Managed Extensibility
// Framework). Commands are discovered at runtime from:
//
//   • Built-in assembly  — GeoAssets.Commands.Builtin
//   • External plugin    — GeoAssets.Plugin.Hydrology (simulated via AssemblyCatalog)
//   • plugins/ folder    — GeoPluginContainer also scans for GeoAssets.Plugin.*.dll
//
// Executed commands:
//   nearby-features       (Spatial)   — repository query via command dispatch
//   shortest-path         (Topology)  — Dijkstra via command dispatch
//   has-cycles            (Topology)  — cycle detection via command dispatch
//   connected-components  (Topology)  — component grouping via command dispatch
//   flood-zone-analysis   (Hydrology) — external plugin demo
// ────────────────────────────────────────────────────────────────────────────
using GeoAssets.Commands;
using GeoAssets.Commands.Builtin.Spatial;
using GeoAssets.Commands.Contracts;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Plugin.Hydrology;

namespace GeoAssets.Examples.Commands;

public static class PluginCommands
{
    public static async Task RunAsync()
    {
        // ── 1. Build a small hydraulic network ──────────────────────────────────

        var pump     = Node("Bomba Principal",   -66.90, 10.48);
        var valve1   = Node("Válvula Norte",     -66.89, 10.50);
        var valve2   = Node("Válvula Sur",       -66.89, 10.46);
        var tank     = Node("Tanque Elevado",    -66.87, 10.50);
        var district = Node("Distrito Residencial", -66.85, 10.48);

        Connect(pump,   valve1,   "water-flow", 0.8);
        Connect(pump,   valve2,   "water-flow", 1.2);
        Connect(valve1, tank,     "water-flow", 0.5);
        Connect(tank,   district, "water-flow", 0.3);

        var repo = new InMemoryAssetRepository();
        foreach (var f in new[] { pump, valve1, valve2, tank, district })
            repo.Add(f);

        // ── 2. Create MEF container ──────────────────────────────────────────────
        //
        // In production, pass pluginsDirectory to pick up external DLLs at runtime.
        // Here we load the Hydrology plugin directly via AssemblyCatalog.

        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");

        using var container = new GeoPluginContainer(
            pluginsDirectory: pluginsDir,
            builtInAssemblies:
            [
                typeof(NearbyFeaturesHandler).Assembly,  // GeoAssets.Commands.Builtin
                typeof(FloodZoneAnalysisHandler).Assembly // GeoAssets.Plugin.Hydrology
            ]);

        var context = new GeoCommandContext(repo);

        // ── 3. List all available commands ──────────────────────────────────────

        Print.Section("Comandos registrados (built-in + plugins)");
        foreach (var cmd in container.GetAvailableCommands())
            Console.WriteLine($"      [{cmd.Category,-12}]  {cmd.Name,-25}  {cmd.Description}");

        // ── 4. nearby-features ───────────────────────────────────────────────────

        Print.Section("nearby-features — nodos dentro de 0.05° de la Bomba Principal");
        var nearbyResult = await container.ExecuteAsync("nearby-features", context,
            new Dictionary<string, object>
            {
                ["center"]        = pump.Geometry!,
                ["radiusDegrees"] = 0.05
            });

        PrintResult(nearbyResult, r => Print.List("Cercanos", (IReadOnlyList<GeoFeature>)r));

        // ── 5. shortest-path ─────────────────────────────────────────────────────

        Print.Section("shortest-path — Bomba Principal → Distrito Residencial (Dijkstra)");
        var pathResult = await container.ExecuteAsync("shortest-path", context,
            new Dictionary<string, object>
            {
                ["fromId"] = pump.Id,
                ["toId"]   = district.Id
            });

        PrintResult(pathResult, r => Print.Path((IReadOnlyList<GeoFeature>)r));

        // ── 6. has-cycles ────────────────────────────────────────────────────────

        Print.Section("has-cycles — ¿la red tiene ciclos?");
        var cycleResult = await container.ExecuteAsync("has-cycles", context);
        PrintResult(cycleResult, r => Print.Bool("HasCycles", (bool)r));

        // ── 7. connected-components ──────────────────────────────────────────────

        Print.Section("connected-components — componentes conectados de la red");
        var compResult = await container.ExecuteAsync("connected-components", context);
        PrintResult(compResult, r =>
            Print.Components((IReadOnlyList<IReadOnlyList<GeoFeature>>)r));

        // ── 8. flood-zone-analysis (external plugin) ─────────────────────────────

        Print.Section("flood-zone-analysis — activos dentro de zona de inundación [PLUGIN]");

        // A polygon covering the southern part of the network
        var floodZone = new GeoPolygon
        {
            Coordinates =
            [
                [
                    [-66.92, 10.44], [-66.84, 10.44],
                    [-66.84, 10.49], [-66.92, 10.49],
                    [-66.92, 10.44]
                ]
            ]
        };

        var floodResult = await container.ExecuteAsync("flood-zone-analysis", context,
            new Dictionary<string, object> { ["floodZone"] = floodZone });

        PrintResult(floodResult, r =>
            Print.List("En zona de riesgo", (IReadOnlyList<GeoFeature>)r));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static GeoFeature Node(string name, double lon, double lat) =>
        new()
        {
            Properties = new() { Name = name, AssetTypeId = AssetType.Point.Id.ToString() },
            Geometry   = new GeoPoint { Coordinates = [lon, lat] }
        };

    private static void Connect(GeoFeature from, GeoFeature to, string kind, double weight) =>
        from.Topology.Add(new TopoEdge { TargetId = to.Id, Kind = kind, Weight = weight });

    private static void PrintResult(GeoCommandResult result, Action<object> onSuccess)
    {
        if (!result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"      Error: {result.Error}");
            Console.ResetColor();
            return;
        }
        onSuccess(result.Data!);
    }
}
