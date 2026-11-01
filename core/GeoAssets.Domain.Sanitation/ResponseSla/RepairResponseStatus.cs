namespace GeoAssets.Domain.Sanitation.ResponseSla;

/// <summary>Where a running <see cref="RepairResponseClock"/> stands relative to its deadline, as of the moment it was read.</summary>
public enum RepairResponseStatus
{
    /// <summary>Not yet at the warning threshold.</summary>
    OnTrack,

    /// <summary>Past the warning threshold but not yet overdue.</summary>
    AtRisk,

    /// <summary>Past the deadline. Still open — nothing here implies the repair resolved late, only that the clock is.</summary>
    Overdue,
}
