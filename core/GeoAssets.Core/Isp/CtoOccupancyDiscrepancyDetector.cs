namespace GeoAssets.Core.Isp;

/// <summary>Builds a validated <see cref="CtoOccupancyDiscrepancy"/> (XD01-281).</summary>
public static class CtoOccupancyDiscrepancyDetector
{
    public static CtoOccupancyDiscrepancy Detect(string ctoFeatureId, int bssReportedOccupied, int geoAssetsTrackedOccupied)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ctoFeatureId);
        ArgumentOutOfRangeException.ThrowIfNegative(bssReportedOccupied);
        ArgumentOutOfRangeException.ThrowIfNegative(geoAssetsTrackedOccupied);

        return new CtoOccupancyDiscrepancy(ctoFeatureId, bssReportedOccupied, geoAssetsTrackedOccupied);
    }
}
