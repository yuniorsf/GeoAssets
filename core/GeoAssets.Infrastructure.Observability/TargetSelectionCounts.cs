namespace GeoAssets.Infrastructure.Observability;

/// <summary>
/// The outcome counts of one service-order target selection, as tagged on its span by
/// <see cref="GeoAssetsActivitySource.SetTargetSelectionCounts"/> (XD01-308). Counts only: no
/// identifier, coordinate or organization value belongs here.
/// </summary>
public sealed record TargetSelectionCounts(
    int Matched,
    int Eligible,
    int AlreadyInOrder,
    int Truncated,
    int Warnings,
    int Blocked);
