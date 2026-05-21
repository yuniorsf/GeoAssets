namespace GeoAssets.Core.Agents;

public abstract class MultiAgentOrchestrator : IMultiAgentOrchestrator
{
    private readonly IReadOnlyDictionary<string, IAgentWorker> _agents;

    protected MultiAgentOrchestrator(IEnumerable<IAgentWorker> agents)
    {
        ArgumentNullException.ThrowIfNull(agents);

        var capabilities = new List<AgentCapability>();
        var registry = new Dictionary<string, IAgentWorker>(StringComparer.Ordinal);

        foreach (var agent in agents)
        {
            ArgumentNullException.ThrowIfNull(agent);

            if (!registry.TryAdd(agent.Name, agent))
                throw new ArgumentException($"An agent named '{agent.Name}' is already registered.", nameof(agents));

            capabilities.Add(new AgentCapability(agent.Name, agent.Description));
        }

        AgentCapabilities = capabilities.AsReadOnly();
        _agents = registry;
    }

    protected IReadOnlyList<AgentCapability> AgentCapabilities { get; }

    public abstract Task<string> RunAsync(string userTask, CancellationToken ct = default);

    protected Task<string> DispatchAsync(string agentName, AgentWorkItem workItem, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        ArgumentNullException.ThrowIfNull(workItem);

        if (!_agents.TryGetValue(agentName, out var agent))
            throw new InvalidOperationException($"Unknown agent: {agentName}");

        return agent.RunAsync(workItem, ct);
    }
}
