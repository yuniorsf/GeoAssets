using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using GeoAssets.Core.Agents;

namespace GeoAssets.Examples.MultiAgent;

internal abstract class AnthropicToolOrchestrator(IEnumerable<IAgentWorker> agents) : MultiAgentOrchestrator(agents)
{
    private const string OrchestratorModel = "claude-opus-4-7";

    private readonly AnthropicClient _client = new();

    protected abstract string SystemPrompt { get; }

    public override async Task<string> RunAsync(string userTask, CancellationToken ct = default)
    {
        var tools = BuildTools();
        var messages = new List<MessageParam>
        {
            new() { Role = Role.User, Content = userTask }
        };

        Message lastResponse;
        while (true)
        {
            lastResponse = await _client.Messages.Create(new MessageCreateParams
            {
                Model = OrchestratorModel,
                MaxTokens = 4096,
                System = SystemPrompt,
                Tools = [.. tools],
                Messages = [.. messages],
            });

            var assistantContent = new List<ContentBlockParam>();
            var toolResults = new List<ContentBlockParam>();

            foreach (var block in lastResponse.Content)
            {
                if (block.TryPickText(out TextBlock? text))
                {
                    assistantContent.Add(new TextBlockParam { Text = text!.Text });
                    continue;
                }

                if (!block.TryPickToolUse(out ToolUseBlock? toolUse))
                    continue;

                assistantContent.Add(new ToolUseBlockParam
                {
                    ID = toolUse!.ID,
                    Name = toolUse.Name,
                    Input = toolUse.Input,
                });

                var result = await DispatchToolAsync(toolUse, ct);

                toolResults.Add(new ToolResultBlockParam
                {
                    ToolUseID = toolUse.ID,
                    Content = result,
                });
            }

            messages.Add(new MessageParam { Role = Role.Assistant, Content = assistantContent });

            if (lastResponse.StopReason != StopReason.ToolUse)
                break;

            messages.Add(new MessageParam { Role = Role.User, Content = toolResults });
        }

        foreach (var block in lastResponse.Content)
            if (block.TryPickText(out TextBlock? text))
                return text!.Text;

        return string.Empty;
    }

    private async Task<string> DispatchToolAsync(ToolUseBlock toolUse, CancellationToken ct)
    {
        var input = toolUse.Input;
        var task = input.TryGetValue("task", out var rawTask) ? rawTask.GetString() ?? "" : "";
        var context = input.TryGetValue("context", out var rawContext) ? rawContext.GetString() ?? "" : "";

        try
        {
            return await DispatchAsync(toolUse.Name, new AgentWorkItem(task, context), ct);
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }
    }

    private List<Tool> BuildTools()
    {
        static Tool AgentTool(AgentCapability capability) => new()
        {
            Name = capability.Name,
            Description = capability.Description,
            InputSchema = InputSchema.FromRawUnchecked(new Dictionary<string, JsonElement>
            {
                ["type"] = JsonDocument.Parse("\"object\"").RootElement.Clone(),
                ["properties"] = JsonDocument.Parse("""
                    {
                        "task":    {"type":"string","description":"The specific task for this agent to perform."},
                        "context": {"type":"string","description":"Relevant output from prior agents to inform this agent."}
                    }
                    """).RootElement.Clone(),
                ["required"] = JsonDocument.Parse("""["task"]""").RootElement.Clone(),
            }),
        };

        return [.. AgentCapabilities.Select(AgentTool)];
    }
}
