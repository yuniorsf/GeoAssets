using System.IO.Compression;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using NetTopologySuite.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace GeoAssets.Provider.Shapefile;

/// <summary>
/// Writes features to an ESRI Shapefile ZIP via <see cref="ShapefileDataWriter"/> — the mirror
/// of <see cref="ShapefileParser"/>'s read path. See <see cref="IShapefileExportService"/> for
/// the per-geometry-type split rationale.
/// </summary>
public sealed class ShapefileExportService : IShapefileExportService
{
    // Standard ESRI WKT for unprojected WGS-84 — every GeoAssets geometry is SRID 4326, and
    // ShapefileDataWriter doesn't write a .prj on its own (confirmed by the sibling test fixture
    // in ShapefileFeatureStoreSpatialQueryTests, which zips only the writer's own .shp/.shx/.dbf
    // output), so a reader opening this archive in QGIS/ArcGIS without one would see no CRS.
    private const string Wgs84Wkt =
        "GEOGCS[\"GCS_WGS_1984\",DATUM[\"D_WGS_1984\",SPHEROID[\"WGS_1984\",6378137.0,298.257223563]]," +
        "PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]]";

    public byte[] ExportToZip(IReadOnlyList<GeoFeature> features)
    {
        var buckets = new Dictionary<string, List<GeoFeature>>
        {
            ["points"]   = [],
            ["lines"]    = [],
            ["polygons"] = [],
        };

        foreach (var feature in features)
        {
            var geom = feature.Geometry?.NtsGeometry;
            if (geom is null || geom.IsEmpty) continue;

            // Same classification as ShapefileParser.InferAssetTypeId — a single .shp can mix
            // e.g. Polygon and MultiPolygon together, just not across these three categories.
            var bucket = geom.GeometryType switch
            {
                "Point" or "MultiPoint"           => "points",
                "LineString" or "MultiLineString" => "lines",
                "Polygon" or "MultiPolygon"       => "polygons",
                _                                  => null,
            };
            if (bucket is not null) buckets[bucket].Add(feature);
        }

        if (buckets.Values.All(b => b.Count == 0))
            throw new InvalidOperationException("No exportable Point/LineString/Polygon geometries found.");

        var tempDir = Path.Combine(Path.GetTempPath(), "shp_export_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var factory = new GeometryFactory(new PrecisionModel(), 4326);
            foreach (var (baseName, bucketFeatures) in buckets)
            {
                if (bucketFeatures.Count > 0)
                    WriteBucket(tempDir, baseName, bucketFeatures, factory);
            }

            var zipPath = Path.Combine(tempDir, "export.zip");
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.GetFiles(tempDir))
                {
                    if (file == zipPath) continue;
                    zip.CreateEntryFromFile(file, Path.GetFileName(file));
                }
            }

            return File.ReadAllBytes(zipPath);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static void WriteBucket(
        string tempDir, string baseName, List<GeoFeature> features, GeometryFactory factory)
    {
        var columnMap = BuildColumnNameMap(features.SelectMany(f => f.Properties.CustomAttributes.Keys).Distinct());

        var header = new DbaseFileHeader();
        header.AddColumn("NAME", 'C', 254, 0);
        foreach (var dbfName in columnMap.Values)
            header.AddColumn(dbfName, 'C', 254, 0);
        header.NumRecords = features.Count;

        var ntsFeatures = new List<IFeature>(features.Count);
        foreach (var feature in features)
        {
            // Every record gets every column (empty string when a given feature lacks that
            // custom attribute) — ShapefileDataWriter looks values up by column name per
            // feature, so a sparse AttributesTable would fail on records missing a key another
            // feature in the same bucket happens to have.
            var attrs = new AttributesTable { { "NAME", feature.Properties.Name } };
            foreach (var (originalKey, dbfName) in columnMap)
            {
                feature.Properties.CustomAttributes.TryGetValue(originalKey, out var value);
                attrs.Add(dbfName, value ?? string.Empty);
            }
            ntsFeatures.Add(new Feature(feature.Geometry!.NtsGeometry, attrs));
        }

        var basePath = Path.Combine(tempDir, baseName);
        var writer = new ShapefileDataWriter(basePath, factory) { Header = header };
        writer.Write(ntsFeatures);

        File.WriteAllText(basePath + ".prj", Wgs84Wkt);
    }

    /// <summary>
    /// Maps each CustomAttributes key to a unique, classic-DBF-safe column name (≤10 chars,
    /// letters/digits/underscore only, never colliding with the reserved "NAME" column or with
    /// each other after truncation — e.g. two keys that both truncate to "MAINTENANC" get a
    /// numeric suffix instead of silently overwriting one another's column).
    /// </summary>
    public static Dictionary<string, string> BuildColumnNameMap(IEnumerable<string> customAttributeKeys)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NAME" };
        var map = new Dictionary<string, string>();

        foreach (var key in customAttributeKeys)
        {
            var baseName = SanitizeDbfFieldName(key);
            var candidate = baseName;
            var suffix = 1;
            while (!used.Add(candidate))
            {
                var suffixText = suffix.ToString();
                candidate = baseName[..Math.Min(baseName.Length, 10 - suffixText.Length)] + suffixText;
                suffix++;
            }
            map[key] = candidate;
        }

        return map;
    }

    private static string SanitizeDbfFieldName(string key)
    {
        var upper = key.ToUpperInvariant();
        var cleaned = new string(upper.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (cleaned.Length == 0) cleaned = "FIELD";
        return cleaned.Length > 10 ? cleaned[..10] : cleaned;
    }
}
