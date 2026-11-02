namespace GeoAssets.Core.Isp;

/// <summary>
/// A comparison between a BSS-reported and a GeoAssets-tracked splitter port occupancy count for
/// the same <see cref="IspAssetTypes.Cto"/> (XD01-281's AC: "discrepancy is surfaced, not silently
/// reconciled") — callers report this as-is rather than overwriting either side with the other.
/// </summary>
public sealed record CtoOccupancyDiscrepancy(string CtoFeatureId, int BssReportedOccupied, int GeoAssetsTrackedOccupied)
{
    public int Difference => BssReportedOccupied - GeoAssetsTrackedOccupied;

    public bool HasDiscrepancy => Difference != 0;
}
