namespace GeoAssets.Domain.Electrical.Sla;

/// <summary>Where a running <see cref="SlaClock"/> stands relative to its deadline, as of the moment it was read.</summary>
public enum SlaClockStatus
{
    /// <summary>Not yet at the warning threshold.</summary>
    OnTrack,

    /// <summary>Past the warning threshold but not yet overdue.</summary>
    AtRisk,

    /// <summary>Past the deadline. Still open — nothing here implies the order resolved late, only that the clock is.</summary>
    Overdue,
}
