using System.Globalization;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using NetTopologySuite.IO;
using OSGeo.OGR;
using OSGeo.OSR;

namespace GeoAssets.Provider.Gdal.VectorFiles;

/// <summary>Turns one OGR feature into a <see cref="GeoFeature"/> (XD01-437); see <see cref="OgrVectorFileReader"/>.</summary>
internal static class OgrFeatureMapper
{
    private const int Wgs84 = 4326;

    public static GeoFeature ToGeoFeature(Feature feature, CoordinateTransformation? toWgs84)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        string? name = null;
        string? id = null;
        using var definition = feature.GetDefnRef();
        for (var i = 0; i < feature.GetFieldCount(); i++)
        {
            if (!feature.IsFieldSetAndNotNull(i))
                continue;

            using var field = definition.GetFieldDefn(i);
            var key = field.GetName();
            var value = feature.GetFieldAsString(i);
            attributes[key] = value;
            if (name is null && string.Equals(key, "name", StringComparison.OrdinalIgnoreCase))
                name = value;
            if (id is null && string.Equals(key, "id", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(value))
                id = value;
        }

        return new GeoFeature
        {
            Id = id ?? feature.GetFID().ToString(CultureInfo.InvariantCulture),
            Geometry = Geometry(feature, toWgs84),
            Properties = new GeoFeatureProperties { Name = name ?? string.Empty, CustomAttributes = attributes },
        };
    }

    private static GeoGeometry? Geometry(Feature feature, CoordinateTransformation? toWgs84)
    {
        using var reference = feature.GetGeometryRef();
        if (reference is null || reference.IsEmpty())
            return null;

        using var geometry = reference.Clone();
        if (toWgs84 is not null && geometry.Transform(toWgs84) != 0)
            throw new InvalidDataException($"Feature {feature.GetFID().ToString(CultureInfo.InvariantCulture)} can't be reprojected to EPSG:4326.");

        var wkb = new byte[geometry.WkbSize()];
        _ = geometry.ExportToWkb(wkb, wkbByteOrder.wkbNDR);
        var nts = new WKBReader().Read(wkb);
        nts.SRID = Wgs84;
        return GeoGeometry.FromNts(nts);
    }
}
