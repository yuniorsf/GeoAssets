using Anthropic;
using Anthropic.Models.Messages;
using GeoAssets.Core.Agents;

namespace GeoAssets.Examples.MultiAgent.Agents;

internal abstract class SubAgent : IAgentWorker
{
    protected readonly AnthropicClient Client = new();

    public abstract string Name { get; }
    public abstract string Description { get; }
    protected abstract string SystemPrompt { get; }
    protected abstract string Model { get; }

    public async Task<string> RunAsync(AgentWorkItem workItem, CancellationToken ct = default)
    {
        var content = string.IsNullOrEmpty(workItem.Context)
            ? workItem.Task
            : $"{workItem.Task}\n\nContext from prior agents:\n{workItem.Context}";

        var response = await Client.Messages.Create(new MessageCreateParams
        {
            Model = Model,
            MaxTokens = 1024,
            System = SystemPrompt,
            Messages = [new() { Role = Role.User, Content = content }],
        });

        foreach (var block in response.Content)
            if (block.TryPickText(out TextBlock? text))
                return text!.Text;

        return string.Empty;
    }
}
