using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using Xunit;

namespace GeoAssets.Identity.Tests.Authorization;

/// <summary>XD01-327: the default <see cref="IOrganizationGrantRepository.GetActiveGrantsIssuedByAsync"/>, over <c>GetAllAsync</c>.</summary>
public sealed class OrganizationGrantRepositoryDefaultsTests
{
    private static readonly Guid Resource = new("c0e1ba00-0000-4000-8000-000000000327");
    private static readonly Guid Other = new("7e1d0000-0000-4000-8000-000000000327");
    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);

    private sealed class AllGrants(params OrganizationGrant[] grants) : IOrganizationGrantRepository
    {
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>(grants);

        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<OrganizationGrant?> FindActiveGrantByKeyAsync(OrganizationGrantKey key, CancellationToken ct = default) => throw new NotSupportedException();
        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static OrganizationGrant Grant(string scope, Guid resource, bool isActive = true, DateTime? expiresAt = null) => new()
    {
        GranteeOrganizationId = Other,
        ResourceOrganizationId = resource,
        ScopeId = scope,
        IsActive = isActive,
        ExpiresAt = expiresAt,
    };

    [Fact]
    public async Task IssuedBy_KeepsOnlyTheOrganizationsActiveGrantsThatHaveNotExpiredAtTheGivenTime()
    {
        IOrganizationGrantRepository repository = new AllGrants(
            Grant("never-expires", Resource),
            Grant("expires-later", Resource, expiresAt: Now.AddSeconds(1)),
            Grant("expired-now", Resource, expiresAt: Now),
            Grant("revoked", Resource, isActive: false),
            Grant("another-resource", Other));

        var issued = await repository.GetActiveGrantsIssuedByAsync(Resource, Now);

        issued.Select(g => g.ScopeId).Should().Equal("never-expires", "expires-later");
    }
}
