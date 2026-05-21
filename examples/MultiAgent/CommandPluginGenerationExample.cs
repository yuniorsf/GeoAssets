using System.Text.Json;
using GeoAssets.Commands.Generation;
using GeoAssets.Examples.MultiAgent.Agents;

namespace GeoAssets.Examples.MultiAgent;

public static class CommandPluginGenerationExample
{
    private const string UserTask =
        """
        Create a GeoAssets command plugin that summarizes assets by asset type.

        Requirements:
          - Plugin name should reflect "Asset Type Summary"
          - Command name: asset-type-summary
          - Category: Reporting
          - Optional parameter: layerId (string) to filter the repository before grouping
          - Result payload should include totalFeatures and a summaries array with assetTypeId and count
          - Implement it against the existing GeoAssets command plugin contracts and repository access pattern
        """;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static async Task RunAsync()
    {
        var apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(apiKey))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("  ⚠  ANTHROPIC_API_KEY is not set — skipping command-plugin generation example.");
            Console.ResetColor();
            return;
        }

        var repoRoot = FindRepositoryRoot();
        var outputRoot = Path.Combine(repoRoot, "generated-plugins");
        var commandsProjectPath = Path.Combine(repoRoot, "core", "GeoAssets.Commands", "GeoAssets.Commands.csproj");

        var orchestrator = new AnthropicCommandPluginOrchestrator(
        [
            new AnalysisAgent(),
            new CommandPluginAgent(),
            new ReviewAgent(),
        ]);

        var rawSpec = await orchestrator.RunAsync(UserTask);
        var spec = DeserializeSpec(rawSpec);

        var scaffolder = new GeoCommandPluginScaffolder(commandsProjectPath);
        var generated = await scaffolder.WriteToDirectoryAsync(spec, outputRoot);

        Print.Section("Agent-generated command plugin");
        Console.WriteLine($"      Project : {generated.ProjectDirectory}");
        Console.WriteLine($"      Assembly: {generated.AssemblyName}");
        foreach (var file in generated.Files)
            Console.WriteLine($"      File    : {file.RelativePath}");
    }

    private static GeoCommandPluginSpec DeserializeSpec(string rawSpec)
    {
        var payload = ExtractJsonPayload(rawSpec);
        var spec = JsonSerializer.Deserialize<GeoCommandPluginSpec>(payload, JsonOptions);
        return spec ?? throw new InvalidOperationException("The orchestrator returned an empty command plugin specification.");
    }

    private static string ExtractJsonPayload(string rawSpec)
    {
        if (string.IsNullOrWhiteSpace(rawSpec))
            throw new InvalidOperationException("The orchestrator returned an empty response.");

        var trimmed = rawSpec.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstBrace = trimmed.IndexOf('{');
            var lastBrace = trimmed.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
                return trimmed[firstBrace..(lastBrace + 1)];
        }

        return trimmed;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "GeoAssets.sln")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the GeoAssets repository root.");
    }
}
