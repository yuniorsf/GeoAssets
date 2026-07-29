using FluentAssertions;
using GeoAssets.Workflow.Agents.Identity;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Workflow.Agents.Tests;

public class WorkflowAgentsServiceExtensionsTests
{
    [Fact]
    public void AddWorkflowAgents_FromConfiguration_ResolvesProviderThatKnowsConfiguredAgent()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WorkflowAgents:Agents:agent-hydro-01:RoleNames:0"] = "AutomationAgent",
        }).Build();
        var services = new ServiceCollection();
        services.AddWorkflowAgents(config);
        using var sp = services.BuildServiceProvider();

        var provider = sp.GetRequiredService<IAgentIdentityProvider>();
        provider.Resolve("agent-hydro-01").RoleNames.Should().Contain("AutomationAgent");
    }

    [Fact]
    public void AddWorkflowAgents_InlineOverload_ResolvesConfiguredAgentAsAgentKind()
    {
        var services = new ServiceCollection();
        services.AddWorkflowAgents(opts =>
            opts.Agents["agent-hydro-01"] = new AgentIdentityDescriptor { RoleNames = ["AutomationAgent"] });
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IAgentIdentityProvider>().Resolve("agent-hydro-01").Kind.Should().Be(ActorKind.Agent);
    }

    [Fact]
    public void AddWorkflowAgents_UnknownAgent_ThrowsAtResolveTime()
    {
        var services = new ServiceCollection();
        services.AddWorkflowAgents(_ => { });
        using var sp = services.BuildServiceProvider();

        var act = () => sp.GetRequiredService<IAgentIdentityProvider>().Resolve("nope");

        act.Should().Throw<KeyNotFoundException>();
    }
}
