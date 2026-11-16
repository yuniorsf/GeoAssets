using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
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
}
