using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// XD01-330 — <see cref="OrganizationResourceAccess"/>, the decision <see cref="OrgResourceAuthorizationHandler"/> shares
/// with the routes that decide from the organization alone: the answer's scopes (none, all, or the union of the
/// qualifying grants' scopes) and <see cref="OrganizationResourceScope.Covers"/>. The per-grant qualification matrix is
/// the handler's (<see cref="OrgResourceAuthorizationHandlerTests"/>, <see cref="OrgResourceScopedRoleGrantTests"/>).
/// </summary>
public sealed class OrganizationResourceAccessTests
{
    private const string Reference = "features:reference";

    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000330");
    private static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000330");

    private sealed class Caller(Guid? organizationId, string[] home, string[] onCoelba) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(home.Contains(permissionCode, StringComparer.Ordinal));
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) =>
            Task.FromResult(new AuthorizationContext
            {
                User = new AppUser
                {
                    Id = Guid.NewGuid(), Email = "u@test", DisplayName = "U",
                    CreatedAt = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc), OrganizationId = organizationId,
                },
                Roles = [],
                Claims = [],
                Permissions = home,
                ScopedRoles = new Dictionary<Guid, IReadOnlyList<string>> { [Coelba] = ["Contractor C1"] },
                ScopedPermissions = new Dictionary<Guid, IReadOnlyList<string>> { [Coelba] = onCoelba },
            });
    }

    private sealed class Grants(params OrganizationGrant[] grants) : IOrganizationGrantRepository
    {
        public int Lookups { get; private set; }

        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default)
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

    private static OrganizationGrant Grant(string? scopeId, bool requiresScopedRole = true) => new()
    {
        GranteeOrganizationId = Bimbo,
        ResourceOrganizationId = Coelba,
        ResourceType = nameof(GeoFeature),
        ScopeId = scopeId,
        AllowedActions = [Reference],
        RequiresScopedRole = requiresScopedRole,
        GrantedBy = "admin@coelba.test",
        GrantedAt = new DateTime(2026, 11, 22, 0, 0, 0, DateTimeKind.Utc),
        IsActive = true,
    };

    private static GeoFeature Feature(string? scopeId)
    {
        var feature = new GeoFeature { Properties = new GeoFeatureProperties { OrganizationId = Coelba } };
        if (scopeId is not null) feature.Properties.CustomAttributes["scopeId"] = scopeId;
        return feature;
    }

    private static Task<OrganizationResourceScope> ResolveAsync(Caller caller, Grants grants, Guid? organization = null) =>
        new OrganizationResourceAccess(caller, grants).ResolveAsync(organization ?? Coelba, nameof(GeoFeature), Reference);

    [Fact]
    public async Task Scoped_grants_answer_the_union_of_their_scopes()
    {
        var scope = await ResolveAsync(new Caller(Bimbo, [], [Reference]), new Grants(Grant("municipio-1"), Grant("municipio-2")));

        scope.IsGranted.Should().BeTrue();
        scope.ScopeIds.Should().BeEquivalentTo(["municipio-1", "municipio-2"]);
        scope.Covers(Feature("municipio-2")).Should().BeTrue();
        scope.Covers(Feature("municipio-3")).Should().BeFalse();
        scope.Covers(Feature("MUNICIPIO-1")).Should().BeFalse("scopes compare exactly");
        scope.Covers(Feature(null)).Should().BeFalse();
        scope.Covers(new AssetType { OrganizationId = Coelba }).Should().BeFalse("a resource without scopes never matches a scoped answer");
    }

    [Fact]
    public async Task One_qualifying_unscoped_grant_answers_every_resource()
    {
        var scope = await ResolveAsync(new Caller(Bimbo, [], [Reference]), new Grants(Grant("municipio-1"), Grant(null)));

        scope.Should().BeSameAs(OrganizationResourceScope.All);
        scope.ScopeIds.Should().BeNull();
        scope.Covers(Feature(null)).Should().BeTrue();
        scope.Covers(new AssetType { OrganizationId = Coelba }).Should().BeTrue();
    }

    [Fact]
    public async Task A_grant_the_caller_does_not_qualify_for_adds_no_scope()
    {
        // The unscoped grant is organization-wide and the caller lacks the home permission: only the scoped one counts.
        var scope = await ResolveAsync(new Caller(Bimbo, [], [Reference]), new Grants(Grant("municipio-1"), Grant(null, requiresScopedRole: false)));

        scope.ScopeIds.Should().BeEquivalentTo(["municipio-1"]);
    }

    [Fact]
    public async Task No_qualifying_grant_answers_nothing()
    {
        var scope = await ResolveAsync(new Caller(Bimbo, [Reference], []), new Grants(Grant("municipio-1")));

        scope.Should().BeSameAs(OrganizationResourceScope.None);
        scope.IsGranted.Should().BeFalse();
        scope.Covers(Feature("municipio-1")).Should().BeFalse();
    }

    [Fact]
    public async Task Own_organization_and_unowned_resources_are_decided_by_the_home_permission_without_a_grant_lookup()
    {
        var grants = new Grants(Grant(null));

        (await ResolveAsync(new Caller(Coelba, [Reference], []), grants)).Should().BeSameAs(OrganizationResourceScope.All);
        (await ResolveAsync(new Caller(Coelba, [], []), grants)).Should().BeSameAs(OrganizationResourceScope.None);
        (await ResolveAsync(new Caller(Bimbo, [Reference], []), grants, Guid.Empty)).Should().BeSameAs(OrganizationResourceScope.All);
        (await ResolveAsync(new Caller(Bimbo, [], [Reference]), grants, Guid.Empty)).Should().BeSameAs(OrganizationResourceScope.None);
        grants.Lookups.Should().Be(0);
    }

    [Fact]
    public async Task A_caller_without_an_organization_or_without_the_permission_anywhere_skips_the_lookup()
    {
        var grants = new Grants(Grant(null));

        (await ResolveAsync(new Caller(null, [Reference], [Reference]), grants)).Should().BeSameAs(OrganizationResourceScope.None);
        (await ResolveAsync(new Caller(Bimbo, [], []), grants)).Should().BeSameAs(OrganizationResourceScope.None);
        grants.Lookups.Should().Be(0);
    }

    [Fact]
    public async Task Of_no_scopes_is_none_and_Covers_and_Resolve_guard_their_arguments()
    {
        OrganizationResourceScope.Of([]).Should().BeSameAs(OrganizationResourceScope.None);
        FluentActions.Invoking(() => OrganizationResourceScope.All.Covers(null!)).Should().Throw<ArgumentNullException>();

        var access = new OrganizationResourceAccess(new Caller(Bimbo, [], []), new Grants());
        await FluentActions.Awaiting(() => access.ResolveAsync(Coelba, " ", Reference)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => access.ResolveAsync(Coelba, nameof(GeoFeature), "")).Should().ThrowAsync<ArgumentException>();
    }
}
