using FluentAssertions;
using GeoAssets.FieldService.Agents.Identity;
using GeoAssets.FieldService.Agents.Tests.TestDoubles;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.FieldService.Agents.Tests;

public class FieldServiceAgentsServiceExtensionsTests
{
    [Fact]
    public void AddFieldServiceAgents_FromConfiguration_ResolvesProviderThatKnowsConfiguredAgent()
    {
        var (sp, logger) = Build(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["FieldServiceAgents:Agents:agent-hydro-01:RoleNames:0"] = "AutomationAgent",
        });
        using (sp)
        {
            var provider = sp.GetRequiredService<IAgentIdentityProvider>();
            provider.Resolve("agent-hydro-01").RoleNames.Should().Contain("AutomationAgent");
            logger.Entries.Should().BeEmpty();
        }
    }

    [Fact]
    public void AddFieldServiceAgents_OnlyLegacySection_ReadsItAndWarnsOnce()
    {
        var (sp, logger) = Build(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["WorkflowAgents:Agents:agent-hydro-01:RoleNames:0"] = "AutomationAgent",
        });
        using (sp)
        {
            var provider = sp.GetRequiredService<IAgentIdentityProvider>();
            provider.Resolve("agent-hydro-01").RoleNames.Should().Contain("AutomationAgent");
            _ = sp.GetRequiredService<IOptions<AgentIdentityOptions>>().Value;

            logger.Entries.Should().ContainSingle()
                .Which.Should().Match<(LogLevel Level, string Message, Exception? Exception)>(e =>
                    e.Level == LogLevel.Warning
                    && e.Message.Contains("WorkflowAgents", StringComparison.Ordinal)
                    && e.Message.Contains("FieldServiceAgents", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void AddFieldServiceAgents_BothSections_ReadsNewSectionAndWarnsLegacyIgnored()
    {
        var (sp, logger) = Build(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["FieldServiceAgents:Agents:agent-new:RoleNames:0"] = "AutomationAgent",
            ["WorkflowAgents:Agents:agent-old:RoleNames:0"]     = "AutomationAgent",
        });
        using (sp)
        {
            var options = sp.GetRequiredService<IOptions<AgentIdentityOptions>>().Value;

            options.Agents.Keys.Should().BeEquivalentTo(["agent-new"]);
            logger.Entries.Should().ContainSingle()
                .Which.Message.Should().Contain("ignored");
        }
    }

    [Fact]
    public void AddFieldServiceAgents_NoSection_BindsNoAgentsWithoutWarning()
    {
        var (sp, logger) = Build(new Dictionary<string, string?>(StringComparer.Ordinal));
        using (sp)
        {
            sp.GetRequiredService<IOptions<AgentIdentityOptions>>().Value.Agents.Should().BeEmpty();
            logger.Entries.Should().BeEmpty();
        }
    }

    [Fact]
    public void AddFieldServiceAgents_FromConfigurationWithoutLogging_StillBinds()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["WorkflowAgents:Agents:agent-hydro-01:RoleNames:0"] = "AutomationAgent",
        }).Build();
        var services = new ServiceCollection();
        services.AddFieldServiceAgents(config);
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IAgentIdentityProvider>().Resolve("agent-hydro-01").RoleNames.Should().Contain("AutomationAgent");
    }

    [Fact]
    public void AddFieldServiceAgents_InlineOverload_ResolvesConfiguredAgentAsAgentKind()
    {
        var services = new ServiceCollection();
        services.AddFieldServiceAgents(opts =>
            opts.Agents["agent-hydro-01"] = new AgentIdentityDescriptor { RoleNames = ["AutomationAgent"] });
        using var sp = services.BuildServiceProvider();

        sp.GetRequiredService<IAgentIdentityProvider>().Resolve("agent-hydro-01").Kind.Should().Be(ActorKind.Agent);
    }

    [Fact]
    public void AddFieldServiceAgents_UnknownAgent_ThrowsAtResolveTime()
    {
        var services = new ServiceCollection();
        services.AddFieldServiceAgents(_ => { });
        using var sp = services.BuildServiceProvider();

        var act = () => sp.GetRequiredService<IAgentIdentityProvider>().Resolve("nope");

        act.Should().Throw<KeyNotFoundException>();
    }

    private static (ServiceProvider Provider, CapturingLogger<AgentIdentityOptions> Logger) Build(
        Dictionary<string, string?> values)
    {
        var config   = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var logger   = new CapturingLogger<AgentIdentityOptions>();
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<AgentIdentityOptions>>(logger);
        services.AddFieldServiceAgents(config);
        return (services.BuildServiceProvider(), logger);
    }
}
