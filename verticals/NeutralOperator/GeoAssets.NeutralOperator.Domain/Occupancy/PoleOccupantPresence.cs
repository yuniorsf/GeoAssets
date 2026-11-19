namespace GeoAssets.NeutralOperator.Domain.Occupancy;

/// <summary>
/// One occupant organization and the poles, out of a requested set, on which it holds an active
/// contract (XD01-314) — the unit a dispatch fans out by: one notification per presence.
/// </summary>
/// <param name="OccupantOrganizationId">The occupant. Never <see cref="Guid.Empty"/>.</param>
/// <param name="PoleFeatureIds">The poles it occupies under contract: distinct, in ordinal order, never empty.</param>
public sealed record PoleOccupantPresence(Guid OccupantOrganizationId, IReadOnlyList<string> PoleFeatureIds);
