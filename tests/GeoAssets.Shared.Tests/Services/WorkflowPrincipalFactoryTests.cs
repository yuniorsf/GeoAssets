using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Shared.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>
/// XD01-332 — the client-side <see cref="WorkflowPrincipal"/> carries what the organization
/// gate reads, so <see cref="ServiceOrderRules"/> gives the UI the verdict the server gives.
///
/// "The server's principal" below is the one <c>ServerWorkflowPrincipalFactory</c> builds from
/// the same identity and the same grant rows (its mapping is asserted in
/// <c>GeoAssets.Server.Tests</c>); the client's goes the whole client route — the grant rows as
/// <c>GET /api/identity/organization-grants/me</c> serializes them, back into an
/// <see cref="AuthorizationContext"/>, through <see cref="WorkflowPrincipalFactory"/>.
/// </summary>
public class WorkflowPrincipalFactoryTests
{
    private static readonly Guid UserId = new("00000000-0000-4000-8000-000000000332");
    private static readonly Guid OrgA = new("a0a0a0a0-0000-4000-8000-000000000332");
    private static readonly Guid OrgB = new("b0b0b0b0-0000-4000-8000-000000000332");
    private static readonly DateTimeOffset Now = new(2026, 11, 16, 10, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    private sealed class FixedAuthorizationService(AuthorizationContext context) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => Task.FromResult(context);
    }

    private static OrganizationGrant GrantRow(string? scopeId = null, string? resourceType = "ServiceOrder", DateTime? expiresAt = null, string? requiredRole = null)
    {
        var grant = new OrganizationGrant
        {
            GranteeOrganizationId = OrgB,
            ResourceOrganizationId = OrgA,
            ResourceType = resourceType,
            ScopeId = scopeId,
            RequiredRole = requiredRole,
            ExpiresAt = expiresAt,
            GrantedBy = "admin-of-a",
        };
        grant.AllowedActions.AddRange(["serviceorders:view", "serviceorders:annotate"]);
        return grant;
    }

    /// <summary>The principal a client builds: grants cross the wire, then go through the factory.</summary>
    private static Task<WorkflowPrincipal> ClientPrincipalAsync(Guid? organizationId, string[] roles, OrganizationGrant[] serverGrantRows)
    {
        var wire = JsonSerializer.Serialize(serverGrantRows.Select(OwnOrganizationGrantDto.FromModel), Wire);
        var received = JsonSerializer.Deserialize<List<OwnOrganizationGrantDto>>(wire, Wire)!;

        var context = new AuthorizationContext
        {
            User = new AppUser { Id = UserId, Email = "u@example.com", DisplayName = "U", OrganizationId = organizationId, CreatedAt = Now.UtcDateTime },
            Roles = roles,
            Claims = [],
            Permissions = [],
            OrganizationGrants = organizationId is { } own ? [.. received.Select(g => g.ToModel(own))] : [],
        };

        return new WorkflowPrincipalFactory(new FixedAuthorizationService(context)).CreateAsync();
    }

    /// <summary>The principal <c>ServerWorkflowPrincipalFactory</c> builds from the same identity and grant rows.</summary>
    private static WorkflowPrincipal ServerPrincipal(Guid? organizationId, string[] roles, OrganizationGrant[] grantRows) =>
        new(UserId.ToString(), organizationId?.ToString(), roles, [], [])
        {
            OrgGrants = [.. grantRows.Select(g => new WorkflowOrgGrant(
                g.ResourceOrganizationId.ToString(), g.ResourceType, g.AllowedActions, g.RequiredRole, g.ScopeId)
            {
                ExpiresAt = g.ExpiresAt,
            })],
        };

    private static ServiceOrder OrderOfA(params OrderDispatch[] dispatches)
    {
        var order = new ServiceOrder
        {
            Id = "ord-1",
            Status = ServiceOrderStatus.Pending,
            OrganizationId = OrgA,
            CreatedBy = "someone-else",
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["scopeId"] = "municipio-123" },
        };
        order.Dispatches.AddRange(dispatches);
        return order;
    }

    public static TheoryData<string> Relations() =>
    [
        "same organization",
        "other organization, no grant",
        "other organization, grant without scope",
        "other organization, grant matching scope",
        "other organization, grant wrong scope",
        "other organization, grant expired",
        "other organization, grant wrong resource type",
        "other organization, grant requiring a role not held",
        "no organization",
        "dispatch recipient by user",
        "dispatch recipient by organization",
    ];

    [Theory]
    [MemberData(nameof(Relations))]
    public async Task XD01_332_ClientPrincipal_GivesEveryActionTheVerdictTheServersPrincipalGets(string relation)
    {
        ArgumentNullException.ThrowIfNull(relation);
        Guid? organizationId = relation switch { "same organization" => OrgA, "no organization" => null, _ => OrgB };
        OrganizationGrant[] grantRows = relation switch
        {
            "other organization, grant without scope" => [GrantRow()],
            "other organization, grant matching scope" => [GrantRow(scopeId: "municipio-123")],
            "other organization, grant wrong scope" => [GrantRow(scopeId: "municipio-999")],
            "other organization, grant expired" => [GrantRow(expiresAt: Now.UtcDateTime.AddMinutes(-1))],
            "other organization, grant wrong resource type" => [GrantRow(resourceType: "GeoFeature")],
            "other organization, grant requiring a role not held" => [GrantRow(requiredRole: "Auditor")],
            _ => [],
        };
        var order = relation switch
        {
            "dispatch recipient by user" => OrderOfA(new OrderDispatch(UserId.ToString(), DispatchTargetType.User, "d", Now.UtcDateTime)),
            "dispatch recipient by organization" => OrderOfA(new OrderDispatch(OrgB.ToString(), DispatchTargetType.Organization, "d", Now.UtcDateTime)),
            _ => OrderOfA(),
        };
        var passesTheGate = relation is "same organization" or "other organization, grant without scope"
            or "other organization, grant matching scope" or "dispatch recipient by user" or "dispatch recipient by organization";
        var rules = new ServiceOrderRules(timeProvider: new FakeTimeProvider(Now));

        foreach (string[] roles in new string[][] { [], ["FieldTechnician"], ["Supervisor"], ["Administrator"] })
        {
            var client = await ClientPrincipalAsync(organizationId, roles, grantRows);
            var server = ServerPrincipal(organizationId, roles, grantRows);

            rules.PassesOrganizationGate(client, order).Should().Be(passesTheGate);
            foreach (var action in Enum.GetValues<OrderActionType>())
            {
                rules.Evaluate(client, action, order).Should().Be(
                    rules.Evaluate(server, action, order), "client and server must agree on {0} for [{1}] ({2})", action, string.Join(",", roles), relation);
            }
        }
    }

    [Fact]
    public async Task XD01_332_CreateAsync_MapsTheContextsGrantsOntoThePrincipal_FieldForField()
    {
        var expiresAt = Now.UtcDateTime.AddDays(1);

        var principal = await ClientPrincipalAsync(OrgB, ["Supervisor"], [GrantRow("municipio-123", expiresAt: expiresAt, requiredRole: "Supervisor")]);

        principal.UserId.Should().Be(UserId.ToString());
        principal.OrganizationId.Should().Be(OrgB.ToString());
        var grant = principal.OrgGrants.Should().ContainSingle().Subject;
        grant.ResourceOrganizationId.Should().Be(OrgA.ToString());
        grant.ResourceType.Should().Be("ServiceOrder");
        grant.ScopeId.Should().Be("municipio-123");
        grant.RequiredRole.Should().Be("Supervisor");
        grant.AllowedActions.Should().Equal("serviceorders:view", "serviceorders:annotate");
        grant.ExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public async Task XD01_332_CreateAsync_ContextWithoutGrants_YieldsAPrincipalWithNone()
    {
        var principal = await ClientPrincipalAsync(OrgA, [], []);

        principal.OrgGrants.Should().BeEmpty();
    }
}
