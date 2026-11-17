using System.Globalization;

namespace GeoAssets.Core.Isp.NeutralOperator.Occupancy;

/// <summary>
/// The custom attributes a pole feature carries about its committed occupancy (XD01-318): what
/// an approval writes onto the pole through the asset catalog, and what a thematic layer rule
/// matches to colour poles by <see cref="OccupancyClass"/>. Custom attributes are strings, so
/// every value is written in one fixed, culture-invariant spelling.
/// </summary>
public static class OccupancyAssetAttributes
{
    /// <summary>The pole's <see cref="OccupancyClass"/>, by member name (<c>None</c>, <c>Low</c>, <c>High</c>, <c>OverCapacity</c>, <c>Unidentified</c>).</summary>
    public const string ClassKey = "occupancyClass";

    /// <summary>How many attachment points are occupied (<see cref="PoleOccupancy.OccupiedCount"/>), as an invariant integer.</summary>
    public const string OccupiedCountKey = "occupantCount";

    /// <summary><c>true</c> or <c>false</c>: more points occupied than the regulated slots (<see cref="PoleOccupancy.IsOverCapacity"/>).</summary>
    public const string OverCapacityKey = "occupancyOverCapacity";

    /// <summary><c>true</c> or <c>false</c>: some occupied point has no identified occupant (<see cref="PoleOccupancy.HasUnidentifiedOccupant"/>).</summary>
    public const string UnidentifiedKey = "occupancyUnidentified";

    /// <summary>The attribute value written for <paramref name="occupancyClass"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="occupancyClass"/> is not a defined value.</exception>
    public static string ClassValue(OccupancyClass occupancyClass) => Enum.IsDefined(occupancyClass)
        ? occupancyClass.ToString()
        : throw new ArgumentOutOfRangeException(nameof(occupancyClass), occupancyClass, "Unknown occupancy class.");

    /// <summary>
    /// The four attributes for <paramref name="occupancy"/>: its class and, beside it, the flags
    /// the class alone does not carry (a pole can be over capacity and have an unidentified
    /// occupant at once; its class is then <see cref="OccupancyClass.Unidentified"/>).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="occupancy"/> is null.</exception>
    public static IReadOnlyDictionary<string, string> For(PoleOccupancy occupancy)
    {
        ArgumentNullException.ThrowIfNull(occupancy);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ClassKey] = ClassValue(occupancy.Class),
            [OccupiedCountKey] = occupancy.OccupiedCount.ToString(CultureInfo.InvariantCulture),
            [OverCapacityKey] = Flag(occupancy.IsOverCapacity),
            [UnidentifiedKey] = Flag(occupancy.HasUnidentifiedOccupant),
        };
    }

    private static string Flag(bool value) => value ? "true" : "false";
}
