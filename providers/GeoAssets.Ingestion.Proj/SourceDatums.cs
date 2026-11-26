using System.Text;
using OSGeo.OSR;

namespace GeoAssets.Ingestion.Proj;

/// <summary>Identifies a parsed CRS's datum by its EPSG code (GDAL resolves ESRI names to it), else by name.</summary>
internal static class SourceDatums
{
    public static SourceDatum? Identify(SpatialReference crs)
    {
        switch (crs.GetAuthorityCode("DATUM"))
        {
            case "6618":
                return SourceDatum.Sad69;
            case "6674":
                return SourceDatum.Sirgas2000;
            case "6326":
                return SourceDatum.Wgs84;
            case { Length: > 0 }:
                return null;
        }

        return Normalize(crs.GetAttrValue("DATUM", 0)) switch
        {
            "SOUTHAMERICANDATUM1969" or "DSOUTHAMERICAN1969" or "SOUTHAMERICAN1969" or "SAD69" => SourceDatum.Sad69,
            "SIRGAS2000" or "DSIRGAS2000" or "SISTEMADEREFERENCIAGEOCENTRICOPARALASAMERICAS2000" => SourceDatum.Sirgas2000,
            "WGS84" or "WGS1984" or "DWGS1984" or "WORLDGEODETICSYSTEM1984" => SourceDatum.Wgs84,
            _ => null,
        };
    }

    private static string Normalize(string? name)
    {
        var builder = new StringBuilder();
        foreach (var character in name ?? string.Empty)
        {
            if (char.IsAsciiLetterOrDigit(character))
                builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString();
    }
}
