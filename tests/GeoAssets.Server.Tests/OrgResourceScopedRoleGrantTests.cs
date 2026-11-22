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
/// XD01-326 — <see cref="OrgResourceAuthorizationHandler"/> with grants that require a scoped role
/// (<see cref="OrganizationGrant.RequiresScopedRole"/>): the ticket's matrix for an <see cref="IOrgOwnedResource"/>, without
/// the order-type dimension. COELBA grants BIMBO <c>features:read</c> on its features in one município, usable only through
/// roles held on COELBA. Organization-wide grants are <see cref="OrgResourceAuthorizationHandlerTests"/>' and unchanged.
/// </summary>
public class OrgResourceScopedRoleGrantTests
{
    private const string Read = "features:read";
    private const string Scope = "municipio-123";

    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000326");
    private static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000326");
    private static readonly Guid Third = new("7e1d0000-0000-4000-8000-000000000326");

    /// <summary>A BIMBO user: <paramref name="home"/> permissions at home, and <paramref name="onCoelba"/> through a role on COELBA.</summary>
    private sealed class ScopedAuthorizationService(
        string[] home, string[] onCoelba, string[]? homeRoles = null, string[]? rolesOnCoelba = null) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default)
            => Task.FromResult(home.Contains(permissionCode, StringComparer.OrdinalIgnoreCase));

        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default)
            => Task.FromResult(new AuthorizationContext
            {
                User = new AppUser
                {
                    Id = Guid.NewGuid(), Email = "tech@bimbo.test", DisplayName = "Tech",
                    CreatedAt = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc), OrganizationId = Bimbo,
                },
                Roles = homeRoles ?? [],
                Claims = [],
                Permissions = home,
                ScopedRoles = new Dictionary<Guid, IReadOnlyList<string>> { [Coelba] = rolesOnCoelba ?? ["Contractor C1"] },
                ScopedPermissions = new Dictionary<Guid, IReadOnlyList<string>> { [Coelba] = onCoelba },
            });
    }

    /// <summary>Counts the lookups, so a test can prove one is skipped.</summary>
    private sealed class GrantRepository(params OrganizationGrant[] grants) : IOrganizationGrantRepository
    {
        public int Lookups { get; private set; }

        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(
            Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default)
        {
            Lookups++;
            return Task.FromResult<IReadOnlyList<OrganizationGrant>>(
                [.. grants.Where(g => g.GranteeOrganizationId == granteeOrganizationId && g.ResourceOrganizationId == resourceOrganizationId)]);
        }

        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrganizationGrant?> FindActiveGrantByKeyAsync(OrganizationGrantKey key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static OrganizationGrant ScopedGrant(
        Guid? resource = null, string? scopeId = Scope, string? requiredRole = null, bool requiresScopedRole = true,
        string[]? orderTypeIds = null) => new()
    {
        GranteeOrganizationId = Bimbo,
        ResourceOrganizationId = resource ?? Coelba,
        ResourceType = nameof(GeoFeature),
        ScopeId = scopeId,
        AllowedActions = [Read],
        RequiredRole = requiredRole,
        RequiresScopedRole = requiresScopedRole,
        OrderTypeIds = [.. orderTypeIds ?? []],
        GrantedBy = "admin@coelba.test",
        GrantedAt = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc),
        IsActive = true,
    };

    private static GeoFeature Feature(Guid? owner = null, string? scopeId = Scope) => new()
    {
        Properties = new GeoFeatureProperties
        {
            OrganizationId = owner ?? Coelba,
            CustomAttributes = scopeId is null
                ? new(StringComparer.Ordinal)
                : new(StringComparer.Ordinal) { ["scopeId"] = scopeId },
        },
    };

    private static async Task<bool> AuthorizeAsync(
        IGeoAuthorizationService authorization, IOrganizationGrantRepository grants, IOrgOwnedResource resource, string code = Read)
    {
        var requirement = new OrgResourceRequirement(code);
        var context = new AuthorizationHandlerContext(
            [requirement], new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "TestAuth")), resource);
        await new OrgResourceAuthorizationHandler(authorization, grants).HandleAsync(context);
        return context.HasSucceeded;
    }

    // ── The ticket's matrix ───────────────────────────────────────────────────

    [Fact]
    public async Task A_user_holding_the_permission_on_the_owner_is_allowed()
    {
        (await AuthorizeAsync(new ScopedAuthorizationService(home: [], onCoelba: [Read]), new GrantRepository(ScopedGrant()), Feature()))
            .Should().BeTrue();
    }

    [Theory]
    [InlineData("municipio-999")]
    [InlineData(null)]
    public async Task A_resource_outside_the_grant_scope_is_refused(string? resourceScope)
    {
        (await AuthorizeAsync(new ScopedAuthorizationService(home: [], onCoelba: [Read]), new GrantRepository(ScopedGrant()), Feature(scopeId: resourceScope)))
            .Should().BeFalse();
    }

    [Fact]
    public async Task The_permission_held_only_at_home_is_refused()
    {
        (await AuthorizeAsync(new ScopedAuthorizationService(home: [Read], onCoelba: [], homeRoles: ["Administrator"]), new GrantRepository(ScopedGrant()), Feature()))
            .Should().BeFalse();
    }

    [Fact]
    public async Task Without_an_active_grant_the_scoped_role_is_refused()
    {
        // The repository resolves active, unexpired grants only: a revoked or expired grant is simply not returned.
        (await AuthorizeAsync(new ScopedAuthorizationService(home: [], onCoelba: [Read]), new GrantRepository(), Feature()))
            .Should().BeFalse();
    }

    [Fact]
    public async Task A_role_scoped_to_one_organization_grants_nothing_on_a_third_one()
    {
        var grants = new GrantRepository(ScopedGrant(), ScopedGrant(resource: Third));

        (await AuthorizeAsync(new ScopedAuthorizationService(home: [], onCoelba: [Read]), grants, Feature(owner: Third)))
            .Should().BeFalse();
    }

    [Fact]
    public async Task A_permission_held_on_the_owner_that_the_grant_does_not_list_is_refused()
    {
        var authorization = new ScopedAuthorizationService(home: [], onCoelba: [Read, "features:edit"]);

        (await AuthorizeAsync(authorization, new GrantRepository(ScopedGrant()), Feature(), "features:edit")).Should().BeFalse();
    }

    [Fact]
    public async Task A_required_role_is_matched_against_the_roles_held_on_the_owner()
    {
        var grants = new GrantRepository(ScopedGrant(requiredRole: "Contractor C1"));

        (await AuthorizeAsync(new ScopedAuthorizationService(home: [], onCoelba: [Read]), grants, Feature()))
            .Should().BeTrue();
        (await AuthorizeAsync(new ScopedAuthorizationService(home: [], onCoelba: [Read], homeRoles: ["Contractor C1"], rolesOnCoelba: ["Viewer"]), grants, Feature()))
            .Should().BeFalse();
    }

    // ── Organization-wide grants and order types ──────────────────────────────

    [Fact]
    public async Task An_organization_wide_grant_is_still_decided_by_the_home_permission()
    {
        var grants = new GrantRepository(ScopedGrant(requiresScopedRole: false));

        (await AuthorizeAsync(new ScopedAuthorizationService(home: [Read], onCoelba: []), grants, Feature())).Should().BeTrue();
        (await AuthorizeAsync(new ScopedAuthorizationService(home: [], onCoelba: [Read]), grants, Feature()))
            .Should().BeFalse("a role held on the owner does not stand in for the home permission an organization-wide grant needs");
    }

    [Fact]
    public async Task A_grant_narrowed_to_order_types_never_matches_a_resource_that_has_none()
    {
        var grants = new GrantRepository(ScopedGrant(orderTypeIds: ["cut"]), ScopedGrant(requiresScopedRole: false, orderTypeIds: ["cut"]));

        (await AuthorizeAsync(new ScopedAuthorizationService(home: [Read], onCoelba: [Read]), grants, Feature())).Should().BeFalse();
    }

    [Fact]
    public async Task Holding_the_permission_neither_at_home_nor_on_the_owner_skips_the_grant_lookup()
    {
        var grants = new GrantRepository(ScopedGrant());

        (await AuthorizeAsync(new ScopedAuthorizationService(home: [], onCoelba: []), grants, Feature())).Should().BeFalse();
        grants.Lookups.Should().Be(0);
    }

    [Fact]
    public async Task The_owners_own_members_and_unowned_resources_still_need_the_home_permission()
    {
        var onlyOnCoelba = new ScopedAuthorizationService(home: [], onCoelba: [Read]);

        (await AuthorizeAsync(onlyOnCoelba, new GrantRepository(ScopedGrant()), Feature(owner: Bimbo))).Should().BeFalse();
        (await AuthorizeAsync(onlyOnCoelba, new GrantRepository(ScopedGrant()), Feature(owner: Guid.Empty))).Should().BeFalse();
        (await AuthorizeAsync(new ScopedAuthorizationService(home: [Read], onCoelba: []), new GrantRepository(), Feature(owner: Bimbo))).Should().BeTrue();
    }
}
