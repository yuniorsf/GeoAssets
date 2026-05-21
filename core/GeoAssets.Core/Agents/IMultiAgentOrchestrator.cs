namespace GeoAssets.Core.Agents;

public interface IMultiAgentOrchestrator
{
    Task<string> RunAsync(string userTask, CancellationToken ct = default);
}
