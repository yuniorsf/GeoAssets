using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Unit-level coverage of <see cref="OrgResourceAuthorizationHandler"/>'s own evaluation logic
/// (XD01-21). <see cref="EndpointAuthorizationTests"/> proves the subject-only gate is wired
/// into the real endpoints; this proves the resource-based check itself is correct in
/// isolation, including every non-leakage boundary the ticket calls out.
/// </summary>
public class OrgResourceAuthorizationHandlerTests
{
    private sealed class FakeAuthorizationService(
        HashSet<string> permissions, Guid? userOrganizationId, IReadOnlyList<string> roles)
        : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default)
            => Task.FromResult(permissions.Contains(permissionCode));

        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
            => Task.FromResult(new AuthorizationContext
            {
                User = new AppUser
                {
                    Id             = Guid.NewGuid(),
                    Email          = "test@example.com",
                    DisplayName    = "Test",
                    CreatedAt      = DateTime.UtcNow,
                    OrganizationId = userOrganizationId,
                },
                Roles       = roles,
                Claims      = [],
                Permissions = [.. permissions],
            });
    }

    private sealed class FakeOrganizationGrantRepository(IReadOnlyList<OrganizationGrant> grants)
        : IOrganizationGrantRepository
    {
        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(
            Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OrganizationGrant>>(
                [.. grants.Where(g => g.GranteeOrganizationId == granteeOrganizationId
                                    && g.ResourceOrganizationId == resourceOrganizationId)]);

        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(
            Guid granteeOrganizationId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<OrganizationGrant>>(
                [.. grants.Where(g => g.GranteeOrganizationId == granteeOrganizationId)]);

        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static OrgResourceAuthorizationHandler Sut(
        HashSet<string> permissions, Guid? userOrganizationId, IReadOnlyList<string>? roles = null,
        IReadOnlyList<OrganizationGrant>? grants = null) =>
        new(new FakeAuthorizationService(permissions, userOrganizationId, roles ?? []),
            new FakeOrganizationGrantRepository(grants ?? []));

    // Authenticated by default — these tests exercise the requirement's own org/permission/grant
    // logic, not the authentication guard (covered separately below).
    private static AuthorizationHandlerContext Context(OrgResourceRequirement requirement, IOrgOwnedResource resource) =>
        new([requirement], new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "TestAuth")), resource);

    private static OrganizationGrant Grant(
        Guid granteeOrgId, Guid resourceOrgId, IEnumerable<string> allowedActions,
        string? resourceType = null, string? requiredRole = null) => new()
    {
        GranteeOrganizationId  = granteeOrgId,
        ResourceOrganizationId = resourceOrgId,
        ResourceType           = resourceType,
        AllowedActions         = [.. allowedActions],
        RequiredRole           = requiredRole,
        GrantedBy              = "admin@example.com",
        GrantedAt              = DateTime.UtcNow,
        IsActive               = true,
    };

    // ── Anonymous caller ──────────────────────────────────────────────────────

    private sealed class ThrowingAuthorizationService : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) =>
            throw new InvalidOperationException("Must not evaluate permissions for an anonymous caller.");
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task HandleRequirementAsync_AnonymousCaller_DeniesWithoutCallingTheAuthorizationService()
    {
        // Regression: the route-level RequireAuthorization requirement may not have
        // succeeded yet when this handler runs (ASP.NET Core evaluates every requirement's
        // handler regardless), and the production IGeoAuthorizationService throws for an
        // anonymous caller instead of returning false — this must never call it. Fails without
        // the fix (the fake would throw, and HandleAsync would propagate that instead of
        // denying cleanly).
        var handler = new OrgResourceAuthorizationHandler(
            new ThrowingAuthorizationService(), new FakeOrganizationGrantRepository([]));
        var resource = new AssetType { OrganizationId = Guid.NewGuid() };
        var context = new AuthorizationHandlerContext(
            [new OrgResourceRequirement("features:read")], new ClaimsPrincipal(new ClaimsIdentity()), resource);

        var act = () => handler.HandleAsync(context);

        await act.Should().NotThrowAsync();
        context.HasSucceeded.Should().BeFalse();
    }

    // ── Same organization ─────────────────────────────────────────────────────

    [Fact]
    public async Task HandleRequirementAsync_SameOrganizationAndPermission_Succeeds()
    {
        var orgId = Guid.NewGuid();
        var resource = new AssetType { OrganizationId = orgId };
        var handler = Sut(["features:edit"], userOrganizationId: orgId);
        var context = Context(new OrgResourceRequirement("features:edit"), resource);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_SameOrganizationButMissingPermission_DoesNotSucceed()
    {
        // Non-leakage: same-org membership alone must not bypass the permission check.
        var orgId = Guid.NewGuid();
        var resource = new AssetType { OrganizationId = orgId };
        var handler = Sut([], userOrganizationId: orgId);
        var context = Context(new OrgResourceRequirement("features:edit"), resource);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    // ── Unowned resource (Guid.Empty sentinel) ────────────────────────────────

    [Fact]
    public async Task HandleRequirementAsync_UnownedResource_SucceedsRegardlessOfUserOrganization()
    {
        var resource = new AssetType { OrganizationId = Guid.Empty };
        var handler = Sut(["features:read"], userOrganizationId: null);
        var context = Context(new OrgResourceRequirement("features:read"), resource);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    // ── Different organization, no grant ──────────────────────────────────────

    [Fact]
    public async Task HandleRequirementAsync_DifferentOrganizationNoGrant_DoesNotSucceed()
    {
        // Non-leakage: holding the permission is not enough across an org boundary.
        var handler = Sut(["features:edit"], userOrganizationId: Guid.NewGuid());
        var resource = new AssetType { OrganizationId = Guid.NewGuid() };
        var context = Context(new OrgResourceRequirement("features:edit"), resource);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_UserHasNoOrganization_DoesNotSucceedAgainstOwnedResource()
    {
        var resource = new AssetType { OrganizationId = Guid.NewGuid() };
        var handler = Sut(["features:edit"], userOrganizationId: null);
        var context = Context(new OrgResourceRequirement("features:edit"), resource);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    // ── Different organization, with a grant ──────────────────────────────────

    [Fact]
    public async Task HandleRequirementAsync_MatchingActiveGrant_Succeeds()
    {
        var granteeOrgId = Guid.NewGuid();
        var resourceOrgId = Guid.NewGuid();
        var resource = new AssetType { OrganizationId = resourceOrgId };
        var grant = Grant(granteeOrgId, resourceOrgId, ["features:edit"]);
        var handler = Sut(["features:edit"], userOrganizationId: granteeOrgId, grants: [grant]);
        var context = Context(new OrgResourceRequirement("features:edit"), resource);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_GrantForDifferentResourceType_DoesNotSucceed()
    {
        // Non-leakage: a grant scoped to "ServiceOrder" must not authorize a GeoFeature/AssetType.
        var granteeOrgId = Guid.NewGuid();
        var resourceOrgId = Guid.NewGuid();
        var resource = new AssetType { OrganizationId = resourceOrgId };
        var grant = Grant(granteeOrgId, resourceOrgId, ["features:edit"], resourceType: "ServiceOrder");
        var handler = Sut(["features:edit"], userOrganizationId: granteeOrgId, grants: [grant]);
        var context = Context(new OrgResourceRequirement("features:edit"), resource);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_GrantMissingRequestedAction_DoesNotSucceed()
    {
        // Non-leakage: a grant for "features:read" must not also unlock "features:edit".
        var granteeOrgId = Guid.NewGuid();
        var resourceOrgId = Guid.NewGuid();
        var resource = new AssetType { OrganizationId = resourceOrgId };
        var grant = Grant(granteeOrgId, resourceOrgId, ["features:read"]);
        var handler = Sut(["features:edit"], userOrganizationId: granteeOrgId, grants: [grant]);
        var context = Context(new OrgResourceRequirement("features:edit"), resource);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_GrantRequiresRoleUserLacks_DoesNotSucceed()
    {
        // Non-leakage: RequiredRole is an extra gate, not decoration.
        var granteeOrgId = Guid.NewGuid();
        var resourceOrgId = Guid.NewGuid();
        var resource = new AssetType { OrganizationId = resourceOrgId };
        var grant = Grant(granteeOrgId, resourceOrgId, ["features:edit"], requiredRole: "Supervisor");
        var handler = Sut(["features:edit"], userOrganizationId: granteeOrgId, roles: ["FieldTechnician"], grants: [grant]);
        var context = Context(new OrgResourceRequirement("features:edit"), resource);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }

    [Fact]
    public async Task HandleRequirementAsync_GrantRequiresRoleUserHolds_Succeeds()
    {
        var granteeOrgId = Guid.NewGuid();
        var resourceOrgId = Guid.NewGuid();
        var resource = new AssetType { OrganizationId = resourceOrgId };
        var grant = Grant(granteeOrgId, resourceOrgId, ["features:edit"], requiredRole: "Supervisor");
        var handler = Sut(["features:edit"], userOrganizationId: granteeOrgId, roles: ["Supervisor"], grants: [grant]);
        var context = Context(new OrgResourceRequirement("features:edit"), resource);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task HandleRequirementAsync_GrantForDifferentGranteeOrganization_DoesNotSucceed()
    {
        // Non-leakage: a grant issued to org C must not authorize org B's users.
        var resourceOrgId = Guid.NewGuid();
        var otherGranteeOrgId = Guid.NewGuid();
        var callerOrgId = Guid.NewGuid();
        var resource = new AssetType { OrganizationId = resourceOrgId };
        var grant = Grant(otherGranteeOrgId, resourceOrgId, ["features:edit"]);
        var handler = Sut(["features:edit"], userOrganizationId: callerOrgId, grants: [grant]);
        var context = Context(new OrgResourceRequirement("features:edit"), resource);

        await handler.HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
    }
}
