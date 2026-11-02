using System.Globalization;

namespace GeoAssets.Core.Isp;

/// <summary>
/// Computes <see cref="IspAssetTypes.Cto"/>'s splitter-port occupancy percentage from a
/// <c>GeoFeatureProperties.CustomAttributes</c> dictionary — the "queryable capacity
/// percentage" XD01-272's AC asks for, derived on demand rather than stored redundantly
/// alongside the two raw counts (same reasoning as
/// <c>GeoAssets.Workflow.Infrastructure.ManholeAudit.SubDuctOccupancyPercentage</c>).
/// </summary>
public static class CtoCapacityCalculator
{
    /// <summary><see cref="IspAssetTypes.Cto"/>'s total splitter port count attribute key.</summary>
    public const string TotalSplitterPortsKey = "totalSplitterPorts";

    /// <summary><see cref="IspAssetTypes.Cto"/>'s occupied splitter port count attribute key.</summary>
    public const string OccupiedSplitterPortsKey = "occupiedSplitterPorts";

    /// <summary>
    /// Percentage (0-100) of splitter ports occupied. Returns 0 when
    /// <see cref="TotalSplitterPortsKey"/> is missing, non-numeric, or non-positive — there is
    /// no real capacity to divide by.
    /// </summary>
    public static double OccupiedPortPercentage(IReadOnlyDictionary<string, string> customAttributes)
    {
        var (occupied, total) = GetOccupiedAndTotal(customAttributes);
        return total <= 0 ? 0 : 100.0 * occupied / total;
    }

    /// <summary>
    /// Raw occupied/total splitter port counts (XD01-281) — the pair <see cref="OccupiedPortPercentage"/>
    /// derives from, exposed directly for callers (occupancy adjustment, heatmap queries) that need
    /// the counts themselves rather than just the percentage.
    /// </summary>
    public static (int Occupied, int Total) GetOccupiedAndTotal(IReadOnlyDictionary<string, string> customAttributes)
    {
        ArgumentNullException.ThrowIfNull(customAttributes);
        return (ParseNonNegativeInt(customAttributes, OccupiedSplitterPortsKey), ParseNonNegativeInt(customAttributes, TotalSplitterPortsKey));
    }

    private static int ParseNonNegativeInt(IReadOnlyDictionary<string, string> attributes, string key) =>
        attributes.TryGetValue(key, out var raw) && int.TryParse(raw, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : 0;
}
