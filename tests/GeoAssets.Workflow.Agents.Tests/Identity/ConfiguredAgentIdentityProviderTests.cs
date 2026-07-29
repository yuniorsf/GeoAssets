using FluentAssertions;
using GeoAssets.Workflow.Agents.Identity;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Workflow.Agents.Tests.Identity;

public class ConfiguredAgentIdentityProviderTests
{
    [Fact]
    public void Resolve_UnregisteredAgentId_ThrowsKeyNotFoundException()
    {
        var provider = new ConfiguredAgentIdentityProvider(Options.Create(new AgentIdentityOptions()));

        var act = () => provider.Resolve("ghost-agent");

        act.Should().Throw<KeyNotFoundException>().WithMessage("*ghost-agent*");
    }

    [Fact]
    public void Resolve_RegisteredAgent_ReturnsPrincipalWithAgentKindAndClaims()
    {
        var options = new AgentIdentityOptions
        {
            Agents =
            {
                ["agent-1"] = new AgentIdentityDescriptor
                {
                    OrganizationId  = "org-1",
                    RoleNames       = ["AutomationAgent"],
                    GroupIds        = ["crew-1"],
                    PermissionCodes = ["serviceorders:create"],
                }
            }
        };
        var provider = new ConfiguredAgentIdentityProvider(Options.Create(options));

        var principal = provider.Resolve("agent-1");

        principal.Kind.Should().Be(ActorKind.Agent);
        principal.UserId.Should().Be("agent-1");
        principal.HasRole("AutomationAgent").Should().BeTrue();
        principal.BelongsToOrganization("org-1").Should().BeTrue();
        principal.BelongsToGroup("crew-1").Should().BeTrue();
        principal.HasPermission("serviceorders:create").Should().BeTrue();
    }
}
