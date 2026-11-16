using System.IO.Compression;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using MaxRev.Gdal.Core;
using OSGeo.OGR;
using OSGeo.OSR;

namespace GeoAssets.Provider.FileGeodatabase;

/// <summary>
/// Writes BDGD layers to a real ESRI File Geodatabase via GDAL's OGR "OpenFileGDB" driver (the
/// only write-capable FileGDB driver available without Esri's proprietary SDK), then zips the
/// resulting .gdb directory for transport. Mirrors
/// <see cref="GeoAssets.Provider.Shapefile.ShapefileExportService"/>'s shape — one writer per
/// container, every attribute key sanitized into a safe field name — but FileGDB holds all
/// layers in a single container instead of one Shapefile triple per geometry type.
/// </summary>
public sealed class FileGeodatabaseExportService : IFileGeodatabaseExportService
{
    private static readonly Lock ConfigureLock = new();
    private static bool _gdalConfigured;

    public byte[] ExportToZip(
        IReadOnlyDictionary<string, IReadOnlyList<GeoFeature>> layeredFeatures,
        string targetCrsWkt,
        int targetSrid)
    {
        ArgumentNullException.ThrowIfNull(layeredFeatures);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetCrsWkt);

        if (layeredFeatures.Values.All(features => features.Count == 0))
            throw new InvalidOperationException("No features provided for any layer.");

        EnsureGdalConfigured();

        var tempDir = Path.Combine(Path.GetTempPath(), "fgdb_export_" + Guid.NewGuid().ToString("N"));
        var gdbPath = Path.Combine(tempDir, "export.gdb");
        Directory.CreateDirectory(tempDir);
        try
        {
            using var srs = new SpatialReference(null);
            srs.ImportFromWkt(ref targetCrsWkt);

            using var driver = Ogr.GetDriverByName("OpenFileGDB")
                ?? throw new InvalidOperationException("GDAL's OpenFileGDB driver is not available in this build.");
            using var dataSource = driver.CreateDataSource(gdbPath, [])
                ?? throw new InvalidOperationException($"Failed to create File Geodatabase at '{gdbPath}'.");

            foreach (var (layerName, features) in layeredFeatures)
            {
                if (features.Count > 0)
                    WriteLayer(dataSource, layerName, features, srs);
            }

            dataSource.FlushCache();

            var zipPath = Path.Combine(tempDir, "export.zip");
            ZipFile.CreateFromDirectory(gdbPath, zipPath, CompressionLevel.Optimal, includeBaseDirectory: true);
            return File.ReadAllBytes(zipPath);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// GDAL's native drivers must be configured once per process before any OGR call — safe to
    /// call repeatedly, guarded the same way <c>GdalBase.ConfigureAll()</c>'s own docs recommend.
    /// </summary>
    private static void EnsureGdalConfigured()
    {
        lock (ConfigureLock)
        {
            if (_gdalConfigured) return;
            GdalBase.ConfigureAll();
            _gdalConfigured = true;
        }
    }

    private static void WriteLayer(DataSource dataSource, string layerName, IReadOnlyList<GeoFeature> features, SpatialReference srs)
    {
        var geometryType = features[0].Geometry switch
        {
            GeoPoint => wkbGeometryType.wkbPoint,
            GeoLineString => wkbGeometryType.wkbLineString,
            _ => throw new InvalidOperationException(
                $"Unsupported geometry type for FileGDB layer '{layerName}': '{features[0].Geometry?.GeometryType}'."),
        };

        using var layer = dataSource.CreateLayer(layerName, srs, geometryType, [])
            ?? throw new InvalidOperationException($"Failed to create FileGDB layer '{layerName}'.");

        var columnMap = BuildFieldNameMap(features.SelectMany(f => f.Properties.CustomAttributes.Keys).Distinct(StringComparer.Ordinal));

        using var nameField = new FieldDefn("NAME", FieldType.OFTString);
        nameField.SetWidth(254);
        layer.CreateField(nameField, 1);
        foreach (var fieldName in columnMap.Values)
        {
            using var fieldDefn = new FieldDefn(fieldName, FieldType.OFTString);
            fieldDefn.SetWidth(254);
            layer.CreateField(fieldDefn, 1);
        }

        var layerDefn = layer.GetLayerDefn();
        foreach (var feature in features)
        {
            using var ogrFeature = BuildOgrFeature(layerDefn, feature, columnMap, layerName);
            layer.CreateFeature(ogrFeature);
        }
    }

    /// <summary>
    /// Builds one fully-populated <see cref="OSGeo.OGR.Feature"/>, transferring dispose
    /// ownership to the caller on success — on any failure while populating it, disposes it here
    /// instead (the try/finally "ownership transfer" pattern CA2000 documents for APIs that
    /// create-then-return a disposable).
    /// </summary>
    private static OSGeo.OGR.Feature BuildOgrFeature(
        FeatureDefn layerDefn, GeoFeature feature, Dictionary<string, string> columnMap, string layerName)
    {
        var ogrFeature = new OSGeo.OGR.Feature(layerDefn);
        try
        {
            ogrFeature.SetField("NAME", feature.Properties.Name);
            foreach (var (originalKey, fieldName) in columnMap)
            {
                feature.Properties.CustomAttributes.TryGetValue(originalKey, out var value);
                ogrFeature.SetField(fieldName, value ?? string.Empty);
            }

            using var geometry = BuildOgrGeometry(feature.Geometry!, layerName);
            ogrFeature.SetGeometry(geometry);
            return ogrFeature;
        }
        catch
        {
            ogrFeature.Dispose();
            throw;
        }
    }

    private static Geometry BuildOgrGeometry(GeoGeometry geoGeometry, string layerName)
    {
        switch (geoGeometry)
        {
            case GeoPoint point:
                var ogrPoint = new Geometry(wkbGeometryType.wkbPoint);
                ogrPoint.AddPoint_2D(point.Coordinates[0], point.Coordinates[1]);
                return ogrPoint;
            case GeoLineString line:
                var ogrLine = new Geometry(wkbGeometryType.wkbLineString);
                foreach (var coordinate in line.Coordinates)
                    ogrLine.AddPoint_2D(coordinate[0], coordinate[1]);
                return ogrLine;
            default:
                throw new InvalidOperationException(
                    $"Unsupported geometry type for FileGDB layer '{layerName}': '{geoGeometry.GeometryType}'.");
        }
    }

    /// <summary>
    /// Maps each CustomAttributes key to a unique, FileGDB-safe field name — OpenFileGDB allows
    /// up to 64 letters/digits/underscores (far looser than classic DBF's 10-char cap in
    /// <see cref="GeoAssets.Provider.Shapefile.ShapefileExportService.BuildColumnNameMap"/>, so
    /// this is a separate, FileGDB-specific sanitizer rather than a shared one).
    /// </summary>
    public static Dictionary<string, string> BuildFieldNameMap(IEnumerable<string> customAttributeKeys)
    {
        ArgumentNullException.ThrowIfNull(customAttributeKeys);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "NAME" };
        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in customAttributeKeys)
        {
            var baseName = SanitizeFieldName(key);
            var candidate = baseName;
            var suffix = 1;
            while (!used.Add(candidate))
            {
                var suffixText = suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
                candidate = baseName[..Math.Min(baseName.Length, 64 - suffixText.Length)] + suffixText;
                suffix++;
            }
            map[key] = candidate;
        }

        return map;
    }

    private static string SanitizeFieldName(string key)
    {
        var upper = key.ToUpperInvariant();
        var cleaned = new string(upper.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        if (cleaned.Length == 0) cleaned = "FIELD";
        if (!char.IsLetter(cleaned[0]) && cleaned[0] != '_') cleaned = "F_" + cleaned;
        return cleaned.Length > 64 ? cleaned[..64] : cleaned;
    }
}
