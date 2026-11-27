using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Rendering;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Server.Rendering;
using GeoAssets.Server.Tiles;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Server.Tests.Tiles;

/// <summary>Grants by grantee organization.</summary>
internal sealed class Grants : IOrganizationGrantRepository
{
    public ConcurrentDictionary<Guid, IReadOnlyList<OrganizationGrant>> ByGrantee { get; } = new();

    public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) =>
        Task.FromResult(ByGrantee.TryGetValue(granteeOrganizationId, out var grants) ? grants : []);

    public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<OrganizationGrant>>([]);

    public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<OrganizationGrant?> FindActiveGrantByKeyAsync(OrganizationGrantKey key, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
    public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
    public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) => throw new NotSupportedException();
}
