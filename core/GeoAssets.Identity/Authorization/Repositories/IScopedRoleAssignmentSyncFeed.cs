using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization.Repositories;

/// <summary>
/// The identity host's side of the scoped role assignment sync (XD01-328): the change feed
/// <see cref="IScopedRoleAssignmentSyncClient"/> implementations poll, served by
/// <c>GET /api/identity/scoped-role-assignments/sync</c>.
/// </summary>
public interface IScopedRoleAssignmentSyncFeed
{
    /// <summary>
    /// Up to <paramref name="take"/> entries recorded after <paramref name="afterSequence"/>, ascending, each resolved to what
    /// the user holds on the organization when the batch is read — see <see cref="ScopedRoleAssignmentSyncEntry"/>.
    /// </summary>
    Task<IReadOnlyList<ScopedRoleAssignmentSyncEntry>> GetSyncBatchAsync(
        long afterSequence, int take, CancellationToken ct = default);
}
