using GeoAssets.Identity.Authorization.Models;

namespace GeoAssets.Identity.Authorization;

/// <summary>
/// Client contract for a vertical microservice to pull <see cref="OrganizationGrant"/> changes
/// from the Core Platform's identity host (XD01-290) — the piece that keeps an isolated
/// microservice's own locally-mirrored grant table current for RLS checks that can no longer
/// reach the real <c>OrganizationGrants</c> table across a database boundary. See
/// <c>OrganizationGrantSyncWorker</c> in <c>GeoAssets.Workflow.EFCore</c> for the consumer, and
/// <c>HttpOrganizationGrantSyncClient</c> in <c>GeoAssets.FieldService.Server</c> for the one
/// implementation today.
/// </summary>
public interface IOrganizationGrantSyncClient
{
    /// <summary>
    /// Fetches up to <paramref name="take"/> sync entries recorded after
    /// <paramref name="afterSequence"/>, ordered by ascending
    /// <see cref="OrganizationGrantSyncEntry.SequenceNumber"/>. Pass <c>0</c> for
    /// <paramref name="afterSequence"/> on the very first call.
    /// </summary>
    Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(
        long afterSequence, int take, CancellationToken ct = default);
}
