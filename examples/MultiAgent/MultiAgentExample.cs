using GeoAssets.Examples.MultiAgent.Agents;

// ────────────────────────────────────────────────────────────────────────────
// 07 · Classic Multi-Agent Claude Structure
//
// Demonstrates a core multi-agent orchestrator with Anthropic-backed agents:
//
//   User
//    └─ AnthropicMultiAgentOrchestrator  (claude-opus-4-7)  — breaks the task, dispatches via tools
//         ├─ call_analysis_agent  →  AnalysisAgent  (claude-haiku-4-5)
//         ├─ call_code_agent      →  CodeAgent      (claude-haiku-4-5)
//         └─ call_review_agent    →  ReviewAgent    (claude-haiku-4-5)
//                  └─ results → Orchestrator → synthesized final answer
//
// Requirements:
//   • ANTHROPIC_API_KEY environment variable must be set.
//
// Key SDK patterns shown:
//   • Tool definitions via InputSchema.FromRawUnchecked
//   • Manual tool loop: CreateMessage → dispatch → ToolResultBlockParam → repeat
//   • ContentBlock.TryPickText / TryPickToolUse union discriminators
//   • StopReason.ToolUse sentinel to control the loop
// ────────────────────────────────────────────────────────────────────────────

namespace GeoAssets.Examples.MultiAgent;

public static class MultiAgentExample
{
    private const string UserTask =
        """
        We have a GeoAssets hydraulic network with 6 nodes:
          Pumping Station → Junction A → Zone 1 (3 endpoints)
                                       → Zone 2 (2 endpoints)

        Design a real-time monitoring strategy for this network, including:
          1. Which metrics to collect at each node type
          2. Alert thresholds and escalation logic
          3. A C# sketch of the monitoring loop using GeoAssets topology APIs
        """;

    public static async Task RunAsync()
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  ⚠  ANTHROPIC_API_KEY is not set — skipping multi-agent example.");
            Console.ResetColor();
            return;
        }

        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  Task submitted to orchestrator…\n");
        Console.ResetColor();

        var orchestrator = new AnthropicMultiAgentOrchestrator(
        [
            new AnalysisAgent(),
            new CodeAgent(),
            new ReviewAgent(),
        ]);

        var result = await orchestrator.RunAsync(UserTask);

        Print.Section("Orchestrator final answer");
        Console.WriteLine(Indent(result, 6));
    }

    private static string Indent(string text, int spaces)
    {
        var pad = new string(' ', spaces);
        return string.Join('\n', text.Split('\n').Select(l => pad + l));
    }
}
