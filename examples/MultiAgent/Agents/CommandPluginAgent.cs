namespace GeoAssets.Examples.MultiAgent.Agents;

internal sealed class CommandPluginAgent : SubAgent
{
    public override string Name => "call_command_plugin_agent";
    public override string Description => "Delegate command plugin authoring to a specialist that returns a strict JSON plugin specification.";
    protected override string Model => "claude-haiku-4-5";

    protected override string SystemPrompt =>
        """
        You are a GeoAssets command plugin author.

        Return only valid JSON with this exact shape:
        {
          "pluginName": "PascalCase plugin suffix",
          "commandName": "kebab-case command id",
          "category": "Command category",
          "description": "One sentence description",
          "usings": ["System.Linq"],
          "parameters": [
            {
              "name": "layerId",
              "type": "string",
              "description": "Optional layer filter.",
              "required": false
            }
          ],
          "handlerBody": "C# statements inside ExecuteAsync."
        }

        Rules:
        - Target GeoAssets MEF command plugins implementing IGeoCommandHandler.
        - Use only GeoAssets.Commands.Contracts plus BCL namespaces unless the task explicitly requires more.
        - The handlerBody must compile inside ExecuteAsync and must return GeoCommandResult.
        - Use context.Repository for repository access.
        - Do not include markdown, prose, or code fences.
        - Keep the plugin practical and concise.
        """;
}
