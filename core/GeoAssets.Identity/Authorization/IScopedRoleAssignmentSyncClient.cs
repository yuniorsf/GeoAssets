using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization;

/// <summary>
/// Client contract for a vertical microservice to pull scoped role assignment changes (XD01-325's roles held on another
/// organization) from the Core Platform's identity host (XD01-328) — the twin of <see cref="IOrganizationGrantSyncClient"/>,
/// keeping the host's local scoped-role mirror current. See <c>ScopedRoleAssignmentSyncWorker</c> in
/// <c>GeoAssets.Workflow.EFCore</c> for the consumer and <c>HttpScopedRoleAssignmentSyncClient</c> in
/// <c>GeoAssets.FieldService.Server</c> for the one implementation today.
/// </summary>
public interface IScopedRoleAssignmentSyncClient
{
    /// <summary>
    /// Fetches up to <paramref name="take"/> entries recorded after <paramref name="afterSequence"/>, ordered by ascending
    /// <see cref="ScopedRoleAssignmentSyncEntry.SequenceNumber"/>. Pass <c>0</c> on the very first call.
    /// </summary>
    Task<IReadOnlyList<ScopedRoleAssignmentSyncEntry>> GetSyncBatchAsync(
        long afterSequence, int take, CancellationToken ct = default);
}
