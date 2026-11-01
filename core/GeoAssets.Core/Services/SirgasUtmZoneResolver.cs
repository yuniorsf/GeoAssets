namespace GeoAssets.Core.Services;

/// <summary>
/// Resolves which SIRGAS 2000 / UTM southern-hemisphere zone (EPSG 31978–31985, zones 18S–25S)
/// a longitude falls into — the range Brazil's territory spans, and the range XD01-224 (ANEEL
/// BDGD) names explicitly. Standard UTM zone formula (<c>floor((lon + 180) / 6) + 1</c>), just
/// restricted to the zones that range covers.
/// </summary>
public static class SirgasUtmZoneResolver
{
    private const int MinZone = 18;
    private const int MaxZone = 25;
    private const int MinZoneEpsgCode = 31978;

    /// <summary>The UTM zone number (18–25) <paramref name="longitude"/> falls into.</summary>
    public static int ZoneFor(double longitude)
    {
        var zone = (int)Math.Floor((longitude + 180.0) / 6.0) + 1;
        if (zone < MinZone || zone > MaxZone)
            throw new ArgumentOutOfRangeException(nameof(longitude),
                $"Longitude {longitude} resolves to UTM zone {zone}, outside SIRGAS 2000's documented BDGD range (zones {MinZone}S–{MaxZone}S, EPSG {MinZoneEpsgCode}–{MinZoneEpsgCode + (MaxZone - MinZone)}).");

        return zone;
    }

    /// <summary>The central meridian (degrees) of UTM zone <paramref name="zone"/>.</summary>
    public static double CentralMeridianFor(int zone) => -183.0 + (6.0 * zone);

    /// <summary>The SIRGAS 2000 / UTM zone {zone}S EPSG code for <paramref name="longitude"/>.</summary>
    public static int EpsgCodeFor(double longitude) => MinZoneEpsgCode + (ZoneFor(longitude) - MinZone);
}
