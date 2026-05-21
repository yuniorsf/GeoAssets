using FluentAssertions;
using GeoAssets.Core.Agents;
using Xunit;

namespace GeoAssets.Core.Tests.Agents;

public class MultiAgentOrchestratorTests
{
    [Fact]
    public async Task DispatchAsync_KnownAgent_InvokesRegisteredWorker()
    {
        var worker = new FakeAgentWorker("analysis", "Analyzes work", "analysis-result");
        var orchestrator = new TestOrchestrator(worker);

        var result = await orchestrator.DispatchPublicAsync("analysis", new AgentWorkItem("inspect", "prior"));

        result.Should().Be("analysis-result");
        worker.Received.Should().BeEquivalentTo(new AgentWorkItem("inspect", "prior"));
    }

    [Fact]
    public async Task DispatchAsync_UnknownAgent_ThrowsInvalidOperationException()
    {
        var orchestrator = new TestOrchestrator(new FakeAgentWorker("analysis", "Analyzes work", "ok"));

        var act = async () => await orchestrator.DispatchPublicAsync("review", new AgentWorkItem("inspect"));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Unknown agent: review");
    }

    [Fact]
    public void Constructor_DuplicateNames_ThrowsArgumentException()
    {
        var act = () => new TestOrchestrator(
            new FakeAgentWorker("analysis", "first", "ok"),
            new FakeAgentWorker("analysis", "second", "ok"));

        act.Should().Throw<ArgumentException>()
            .WithMessage("*analysis*already registered*");
    }

    [Fact]
    public void AgentCapabilities_ExposeRegisteredMetadata()
    {
        var orchestrator = new TestOrchestrator(
            new FakeAgentWorker("analysis", "Analyzes work", "ok"),
            new FakeAgentWorker("review", "Reviews work", "ok"));

        orchestrator.AgentCapabilitiesPublic.Should().BeEquivalentTo(
        [
            new AgentCapability("analysis", "Analyzes work"),
            new AgentCapability("review", "Reviews work"),
        ]);
    }

    private sealed class TestOrchestrator(params IAgentWorker[] agents) : MultiAgentOrchestrator(agents)
    {
        public IReadOnlyList<AgentCapability> AgentCapabilitiesPublic => AgentCapabilities;

        public override Task<string> RunAsync(string userTask, CancellationToken ct = default) =>
            DispatchAsync("analysis", new AgentWorkItem(userTask), ct);

        public Task<string> DispatchPublicAsync(string agentName, AgentWorkItem workItem, CancellationToken ct = default) =>
            DispatchAsync(agentName, workItem, ct);
    }

    private sealed class FakeAgentWorker(string name, string description, string result) : IAgentWorker
    {
        public string Name { get; } = name;
        public string Description { get; } = description;
        public AgentWorkItem? Received { get; private set; }

        public Task<string> RunAsync(AgentWorkItem workItem, CancellationToken ct = default)
        {
            Received = workItem;
            return Task.FromResult(result);
        }
    }
}
