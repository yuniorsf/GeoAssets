namespace GeoAssets.Core.Agents;

public interface IAgentWorker
{
    string Name { get; }
    string Description { get; }

    Task<string> RunAsync(AgentWorkItem workItem, CancellationToken ct = default);
}
