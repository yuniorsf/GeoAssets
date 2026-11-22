using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// XD01-332 — what <see cref="ServerWorkflowPrincipalFactory"/> puts on the principal the
/// organization gate reads: the caller's own organization and the grants its organization
/// holds, taken only from the repository member that returns active, unexpired grants.
/// </summary>
public class ServerWorkflowPrincipalFactoryTests
{
    private static readonly Guid UserId = new("00000000-0000-4000-8000-000000000332");
    private static readonly Guid OrgA = new("a0a0a0a0-0000-4000-8000-000000000332");
    private static readonly Guid OrgB = new("b0b0b0b0-0000-4000-8000-000000000332");

    private sealed class FixedAuthorizationService(Guid? organizationId) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => Task.FromResult(new AuthorizationContext
        {
            User = new AppUser { Id = UserId, Email = "u@example.com", DisplayName = "U", OrganizationId = organizationId, CreatedAt = new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc) },
            Roles = ["Supervisor"],
            Claims = [],
            Permissions = ["serviceorders:read"],
        });
    }

    [Fact]
    public async Task XD01_332_CreateAsync_CarriesTheCallersOrganization_AndItsOrganizationsActiveGrants_FieldForField()
    {
        var expiresAt = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var grants = new TestOrganizationGrantRepository(); // only GetActiveGrantsForGranteeAsync is implemented
        var held = new OrganizationGrant
        {
            GranteeOrganizationId = OrgB,
            ResourceOrganizationId = OrgA,
            ResourceType = "ServiceOrder",
            ScopeId = "municipio-123",
            RequiredRole = "Supervisor",
            ExpiresAt = expiresAt,
        };
        held.AllowedActions.Add("serviceorders:view");
        grants.Active.Add(held);
        grants.Active.Add(new OrganizationGrant { GranteeOrganizationId = OrgA, ResourceOrganizationId = OrgB }); // another organization's

        var principal = await new ServerWorkflowPrincipalFactory(new FixedAuthorizationService(OrgB), grants).CreateAsync();

        principal.UserId.Should().Be(UserId.ToString());
        principal.OrganizationId.Should().Be(OrgB.ToString());
        principal.RoleNames.Should().Equal("Supervisor");
        principal.GroupIds.Should().BeEmpty();
        var grant = principal.OrgGrants.Should().ContainSingle().Subject;
        grant.ResourceOrganizationId.Should().Be(OrgA.ToString());
        grant.ResourceType.Should().Be("ServiceOrder");
        grant.ScopeId.Should().Be("municipio-123");
        grant.RequiredRole.Should().Be("Supervisor");
        grant.AllowedActions.Should().Equal("serviceorders:view");
        grant.ExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public async Task XD01_332_CreateAsync_CallerWithNoOrganization_HoldsNoGrants_AndTheRepositoryIsNotAsked()
    {
        var principal = await new ServerWorkflowPrincipalFactory(
            new FixedAuthorizationService(organizationId: null), new NeverCalledOrganizationGrantRepository()).CreateAsync();

        principal.OrganizationId.Should().BeNull();
        principal.OrgGrants.Should().BeEmpty();
    }

    /// <summary>A caller of organization B holding a role on organization A (XD01-325).</summary>
    private sealed class ScopedAuthorizationService : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => Task.FromResult(new AuthorizationContext
        {
            User = new AppUser { Id = UserId, Email = "u@example.com", DisplayName = "U", OrganizationId = OrgB, CreatedAt = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc) },
            Roles = ["Supervisor"],
            Claims = [],
            Permissions = ["serviceorders:read"],
            ScopedRoles = new Dictionary<Guid, IReadOnlyList<string>> { [OrgA] = ["Contractor C1"] },
            ScopedPermissions = new Dictionary<Guid, IReadOnlyList<string>> { [OrgA] = ["serviceorders:view", "serviceorders:execute"] },
        });
    }

    [Fact]
    public async Task XD01_326_CreateAsync_CarriesTheScopedRolesAndPermissions_AndEachGrantsScopedRoleAndOrderTypes()
    {
        var grants = new TestOrganizationGrantRepository();
        grants.Active.Add(new OrganizationGrant
        {
            GranteeOrganizationId = OrgB,
            ResourceOrganizationId = OrgA,
            ResourceType = "ServiceOrder",
            AllowedActions = ["serviceorders:view", "serviceorders:execute"],
            RequiresScopedRole = true,
            OrderTypeIds = ["cut"],
        });

        var principal = await new ServerWorkflowPrincipalFactory(new ScopedAuthorizationService(), grants).CreateAsync();

        var grant = principal.OrgGrants.Should().ContainSingle().Subject;
        grant.RequiresScopedRole.Should().BeTrue();
        grant.OrderTypeIds.Should().Equal("cut");
        principal.RoleNames.Should().Equal("Supervisor");
        principal.PermissionCodes.Should().Equal("serviceorders:read");
        principal.RoleNamesOn(OrgA).Should().Equal("Contractor C1");
        principal.PermissionCodesOn(OrgA).Should().Equal("serviceorders:view", "serviceorders:execute");

        // End to end through the rule engine: the scoped role executes organization A's "cut" order, and only that type.
        var rules = new ServiceOrderRules();
        ServiceOrder Order(string type) => new() { Id = "o", OrderTypeId = type, Status = ServiceOrderStatus.Pending, OrganizationId = OrgA, CreatedBy = "someone" };
        rules.Evaluate(principal, OrderActionType.Execute, Order("cut")).Allowed.Should().BeTrue();
        rules.Evaluate(principal, OrderActionType.Execute, Order("reconnect")).Allowed.Should().BeFalse();
        rules.Evaluate(principal, OrderActionType.Approve, Order("cut")).Allowed
            .Should().BeFalse("the home Supervisor role counts for nothing through a grant that requires a scoped role");
    }
}
