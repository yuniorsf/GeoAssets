namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>
/// EF Core entity mapping to the <c>field_pole_occupancies</c> table (XD01-314): one row per
/// pole that has occupancy on file, holding the version its writers compare. Its entries are
/// <see cref="PoleOccupancyEntryRecord"/> rows. Kept separate from the domain
/// <see cref="Core.Isp.NeutralOperator.Occupancy.PoleOccupancy"/> so the domain model stays free
/// of EF annotations.
/// </summary>
internal sealed class PoleOccupancyRecord
{
    public Guid Id { get; set; }

    public string PoleFeatureId { get; set; } = string.Empty;

    /// <summary>The organization that owns the pole. A pole is on file at most once per organization.</summary>
    public Guid OrganizationId { get; set; } = Guid.Empty;

    /// <summary>
    /// Optimistic-concurrency token managed by <c>EFPoleOccupancyRepository</c>, not by the
    /// database: a write only applies to the row still carrying the version the caller read, and
    /// stores that version plus one. No trigger, so SQLite and PostgreSQL behave the same.
    /// </summary>
    public int Version { get; set; }

    /// <summary>UTC instant of the write that produced <see cref="Version"/>.</summary>
    public DateTime UpdatedAt { get; set; }
}
