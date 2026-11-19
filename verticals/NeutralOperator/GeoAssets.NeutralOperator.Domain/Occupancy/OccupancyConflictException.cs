namespace GeoAssets.NeutralOperator.Domain.Occupancy;

/// <summary>
/// Thrown when a write to a pole's occupancy was based on a <see cref="PoleOccupancy.Version"/>
/// that is no longer the stored one: another writer changed the pole in between (XD01-314).
/// Nothing was written. The caller reloads the pole and decides again.
/// </summary>
public sealed class OccupancyConflictException : InvalidOperationException
{
    /// <summary>The poles whose stored version differs from the expected one. Never null.</summary>
    public IReadOnlyList<string> PoleFeatureIds { get; } = [];

    public OccupancyConflictException(IReadOnlyCollection<string> poleFeatureIds)
        : base(BuildMessage(poleFeatureIds))
    {
        PoleFeatureIds = [.. poleFeatureIds];
    }

    public OccupancyConflictException()
    {
    }

    public OccupancyConflictException(string message) : base(message)
    {
    }

    public OccupancyConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    private static string BuildMessage(IReadOnlyCollection<string> poleFeatureIds)
    {
        ArgumentNullException.ThrowIfNull(poleFeatureIds);
        return $"The occupancy of pole(s) '{string.Join("', '", poleFeatureIds)}' was modified by another writer. Reload and retry.";
    }
}
