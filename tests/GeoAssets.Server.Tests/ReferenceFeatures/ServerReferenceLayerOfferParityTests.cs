using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.ReferenceLayerOfferParity;
using Xunit;
using static GeoAssets.ReferenceLayerOfferParity.ReferenceLayerOfferParityMatrix;

namespace GeoAssets.Server.Tests.ReferenceFeatures;

/// <summary>
/// XD01-331 — the server half of <see cref="ReferenceLayerOfferParityMatrix"/>: <see cref="OrganizationResourceAccess"/>,
/// asked exactly as <c>GET …/organizations/{id}/reference-features</c> asks it (<see cref="ReferenceFeaturesRestApiExtensions.Permission"/>
/// on COELBA's <see cref="GeoFeature"/>s), grants each row as the matrix states. <c>GeoAssets.Shared.Tests</c>'
/// <c>ClientReferenceLayerOfferParityTests</c> asserts the same rows for the map's toggle.
/// </summary>
public sealed class ServerReferenceLayerOfferParityTests
{
    private sealed class FixedAuthorizationService(AuthorizationContext context) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => Task.FromResult(context.HasPermission(permissionCode));
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => Task.FromResult(context);
    }

    /// <summary>The repository's active-grant lookup over a row's stored grants: active, unexpired, grantee and resource matching.</summary>
    private sealed class StoredGrants(IReadOnlyList<OrganizationGrant> stored) : IOrganizationGrantRepository
    {
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>([.. stored.Where(g =>
                g.GranteeOrganizationId == granteeOrganizationId && g.ResourceOrganizationId == resourceOrganizationId &&
                g.IsActive && (g.ExpiresAt is not { } e || e > Now.UtcDateTime))]);

        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrganizationGrant?> FindActiveGrantByKeyAsync(OrganizationGrantKey key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Theory]
    [MemberData(nameof(CaseNames), MemberType = typeof(ReferenceLayerOfferParityMatrix))]
    public async Task XD01_331_TheKernelOpensCoelbasReferenceFeaturesExactlyAsTheMatrixStates(string name)
    {
        var row = Get(name);

        var scope = await new OrganizationResourceAccess(new FixedAuthorizationService(row.Context), new StoredGrants(row.StoredGrants))
            .ResolveAsync(Coelba, nameof(GeoFeature), ReferenceFeaturesRestApiExtensions.Permission);

        scope.IsGranted.Should().Be(row.Offered, row.Name);
    }

    [Fact]
    public void TheMatrixAsksForTheRoutesOwnPermission() =>
        Reference.Should().Be(ReferenceFeaturesRestApiExtensions.Permission);
}
