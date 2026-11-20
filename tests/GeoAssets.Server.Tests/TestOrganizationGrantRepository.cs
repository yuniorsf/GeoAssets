using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Server.Tests;

/// <summary>
/// The one member <c>ServerWorkflowPrincipalFactory</c> reads — a grantee's active grants —
/// backed by a list a test can add to. Every other member is unused by these hosts.
/// </summary>
internal sealed class TestOrganizationGrantRepository : IOrganizationGrantRepository
{
    public List<OrganizationGrant> Active { get; } = [];

    public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrganizationGrant>>([.. Active.Where(g => g.GranteeOrganizationId == granteeOrganizationId)]);

    public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OrganizationGrant?> FindActiveGrantByKeyAsync(OrganizationGrantKey key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) => throw new NotSupportedException();
}
