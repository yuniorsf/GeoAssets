using GeoAssets.Core.VectorFiles;
using GeoAssets.Provider.Gdal.VectorFiles;
using OSGeo.OGR;
using OSGeo.OSR;

namespace GeoAssets.Provider.Gdal.Tests.VectorFiles;

/// <summary>Writes small FlatGeobuf / GeoParquet files with OGR for the tests.</summary>
internal static class TestVectorFiles
{
    /// <summary>
    /// <paramref name="count"/> points named <c>P0</c>…, with an <c>id</c> field <c>pole-0</c>… and an integer <c>kind</c>,
    /// in EPSG:<paramref name="epsg"/>: at (i, −i) degrees for 4326, else at (500 000 + 10 i, 7 800 000) metres. A
    /// FlatGeobuf file keeps that order unless <paramref name="spatialIndex"/> (its writer then sorts the features along
    /// a Hilbert curve).
    /// </summary>
    public static string Write(string directory, VectorFileFormat format, int count, int epsg = 4326, bool spatialIndex = false)
    {
        GdalNativeInitializer.EnsureInitialized();

        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + (format == VectorFileFormat.FlatGeobuf ? ".fgb" : ".parquet"));
        using var driver = Ogr.GetDriverByName(OgrVectorFileReader.DriverName(format));
        using var srs = new SpatialReference(null);
        srs.ImportFromEPSG(epsg);
        srs.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        using (var dataSource = driver.CreateDataSource(path, []))
        {
            string[] options = format == VectorFileFormat.FlatGeobuf ? [$"SPATIAL_INDEX={(spatialIndex ? "YES" : "NO")}"] : [];
            using var layer = dataSource.CreateLayer("poles", srs, wkbGeometryType.wkbPoint, options);
            using (var name = new FieldDefn("name", FieldType.OFTString))
                layer.CreateField(name, 1);
            using (var id = new FieldDefn("id", FieldType.OFTString))
                layer.CreateField(id, 1);
            using (var kind = new FieldDefn("kind", FieldType.OFTInteger))
                layer.CreateField(kind, 1);

            for (var i = 0; i < count; i++)
            {
                using var feature = new Feature(layer.GetLayerDefn());
                feature.SetField("name", $"P{i}");
                feature.SetField("id", $"pole-{i}");
                feature.SetField("kind", i % 3);
                using var point = new Geometry(wkbGeometryType.wkbPoint);
                if (epsg == 4326)
                    point.AddPoint_2D(i, -i);
                else
                    point.AddPoint_2D(500_000 + (10 * i), 7_800_000);
                feature.SetGeometry(point);
                layer.CreateFeature(feature);
            }
        }

        return path;
    }
}
