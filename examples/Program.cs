using GeoAssets.Examples.MultiAgent;
using GeoAssets.Examples.Spatial;
using GeoAssets.Examples.Topology;
using GeoAssets.Examples.Workflow;

Console.OutputEncoding = System.Text.Encoding.UTF8;

// Sync examples keep the original (string, Action) tuple pattern.
// The async plugin example is wrapped in a Task.
var examples = new (string Title, Func<Task> Run)[]
{
    ("01 · Electric Circuit  — directed flow, Dijkstra, topological sort",      () => { ElectricCircuit.Run();  return Task.CompletedTask; }),
    ("02 · Hydraulic Network — BFS routing, connected components, ancestors",   () => { HydraulicNetwork.Run(); return Task.CompletedTask; }),
    ("03 · Road Graph        — bidirectional edges, shortest path, cycle check", () => { RoadGraph.Run();        return Task.CompletedTask; }),
    ("04 · Spatial Queries   — NTS within / intersects / nearby / buffer",      () => { SpatialQueries.Run();   return Task.CompletedTask; }),
    // ("05 · MEF Plugin Commands — command dispatch, built-in + external plugin", PluginCommands.RunAsync),
    ("06 · Custom Selection Strategy — layer-filter + network-impact background process", CustomSelectionStrategy.RunAsync),
    ("07 · Multi-Agent Claude       — Orchestrator + 3 Subagents (requires ANTHROPIC_API_KEY)", MultiAgentExample.RunAsync),
    ("08 · Agent Command Plugin    — Generates a MEF command plugin project (requires ANTHROPIC_API_KEY)", CommandPluginGenerationExample.RunAsync),
};

foreach (var (title, run) in examples)
{
    var bar = new string('═', title.Length + 4);
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.WriteLine($"\n╔{bar}╗");
    Console.WriteLine($"║  {title}  ║");
    Console.WriteLine($"╚{bar}╝");
    Console.ResetColor();

    await run();

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine("\n  Press any key for the next example…");
    Console.ResetColor();
    Console.ReadKey(intercept: true);
}

Console.ForegroundColor = ConsoleColor.Green;
Console.WriteLine("\n✓ All examples completed.\n");
Console.ResetColor();
