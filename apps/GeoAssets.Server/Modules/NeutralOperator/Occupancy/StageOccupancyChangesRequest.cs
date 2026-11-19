using GeoAssets.NeutralOperator.Domain.Occupancy;

namespace GeoAssets.Server.Modules.NeutralOperator.Occupancy;

/// <summary>
/// Body of <c>PUT /service-orders/{id}/targets/{targetId}/occupancy-changes</c>: the changes to
/// stage on the target, replacing whatever is staged there.
/// </summary>
/// <param name="Changes">
/// 1 to <see cref="OccupancyChangeSet.MaxChanges"/> changes. An Add may omit <c>entryId</c>; the
/// host then assigns one (a client that wants a repeated request to produce the same entry id
/// sends its own).
/// </param>
public sealed record StageOccupancyChangesRequest(IReadOnlyList<OccupancyChange?>? Changes);
