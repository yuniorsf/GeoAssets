namespace GeoAssets.NeutralOperator.Persistence.Entities;

/// <summary>
/// EF Core entity mapping to <c>occupancy_apply_saga_poles</c> (XD01-383): a pole an open approval wrote, with its entries
/// before the approval (what a rejection restores) and the version the approval left it at.
/// </summary>
internal sealed class OccupancyApplySagaPoleRecord
{
    public Guid BatchId { get; set; }

    public string PoleFeatureId { get; set; } = string.Empty;

    public int BaselineVersion { get; set; }

    public int AppliedVersion { get; set; }

    /// <summary>JSON array of the pole's entries before the approval.</summary>
    public string BaselineEntriesJson { get; set; } = "[]";

    /// <summary>The pole owner.</summary>
    public Guid OrganizationId { get; set; } = Guid.Empty;
}
