using System.IO.Compression;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;

namespace GeoAssets.Provider.Shapefile;

/// <summary>
/// Parses a ZIP archive containing an ESRI Shapefile (.shp + .dbf, optional .prj — when present,
/// its CRS is reprojected to WGS84; when absent, coordinates are assumed to already be WGS84)
/// into a list of <see cref="GeoFeature"/> objects.
/// </summary>
internal static class ShapefileParser
{
    private static readonly string[] NameFieldCandidates =
        ["NAME", "NOMBRE", "NOM", "NOME", "LABEL", "TITLE", "BEZEICH", "DESCRIPTI"];

    /// <summary>
    /// Decodes a Base-64 ZIP archive and returns the features it contains.
    /// The archive must include at least a <c>.shp</c> and a <c>.dbf</c> file.
    /// </summary>
    internal static IReadOnlyList<GeoFeature> ParseZip(string base64Content)
    {
        var zipBytes = Convert.FromBase64String(base64Content);
        var tempDir = Path.Combine(Path.GetTempPath(), "shp_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            using (var archive = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    var dest = Path.Combine(tempDir, entry.Name);
                    using var src = entry.Open();
                    using var dst = File.Create(dest);
                    src.CopyTo(dst);
                }
            }

            var shpFile = Directory.GetFiles(tempDir, "*.shp").FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "No .shp file found in the archive. " +
                    "Please provide a ZIP containing at least a .shp and a .dbf file.");

            var basePath = Path.ChangeExtension(shpFile, null);
            return ReadFeatures(basePath);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    private static IReadOnlyList<GeoFeature> ReadFeatures(string basePath)
    {
        var factory = new GeometryFactory(new PrecisionModel(), 4326);
        using var reader = new ShapefileDataReader(basePath, factory);

        var toWgs84 = TryCreateWgs84Transform(basePath + ".prj");

        var header = reader.DbaseHeader;
        var nameIdx = FindNameFieldIndex(header);

        var features = new List<GeoFeature>();
        while (reader.Read())
        {
            var ntsGeom = reader.Geometry;
            if (ntsGeom is null || ntsGeom.IsEmpty) continue;
            if (toWgs84 is not null)
            {
                ntsGeom.Apply(new ReprojectFilter(toWgs84));
                ntsGeom.GeometryChanged();
            }

            var attrs = new Dictionary<string, string>(header.NumFields, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < header.NumFields; i++)
            {
                var val = reader.GetValue(i + 1)?.ToString()?.Trim() ?? string.Empty;
                attrs[header.Fields[i].Name] = val;
            }

            var name = nameIdx >= 0
                ? (reader.GetValue(nameIdx + 1)?.ToString()?.Trim() ?? string.Empty)
                : string.Empty;

            features.Add(new GeoFeature
            {
                Geometry = GeoGeometry.FromNts(ntsGeom),
                Properties = new GeoFeatureProperties
                {
                    Name = name,
                    AssetTypeId = InferAssetTypeId(ntsGeom),
                    CustomAttributes = attrs
                }
            });
        }

        return features;
    }

    private static int FindNameFieldIndex(DbaseFileHeader header)
    {
        for (var i = 0; i < header.NumFields; i++)
        {
            if (NameFieldCandidates.Contains(
                    header.Fields[i].Name.ToUpperInvariant()))
                return i;
        }
        return -1;
    }

    private static string InferAssetTypeId(Geometry geom) =>
        geom.GeometryType switch
        {
            "Point" or "MultiPoint" => AssetType.Point.Id.ToString(),
            "LineString" or "MultiLineString" => AssetType.Line.Id.ToString(),
            _ => AssetType.Area.Id.ToString()
        };

    /// <summary>
    /// GeoGeometry.FromNts always treats coordinates as WGS84 lon/lat (SRID 4326), but shapefiles
    /// are commonly exported in a projected CRS (e.g. UTM, in meters) — without reprojecting,
    /// those coordinates land far outside any sane map view, so features import with correct
    /// attributes but never render. When a .prj sidecar is present, build a transform to WGS84;
    /// any failure to parse it falls back to using the raw coordinates as before.
    /// </summary>
    private static MathTransform? TryCreateWgs84Transform(string prjPath)
    {
        if (!File.Exists(prjPath)) return null;

        try
        {
            var sourceCs = new CoordinateSystemFactory().CreateFromWkt(File.ReadAllText(prjPath));
            return new CoordinateTransformationFactory()
                .CreateFromCoordinateSystems(sourceCs, GeographicCoordinateSystem.WGS84)
                .MathTransform;
        }
        catch
        {
            return null;
        }
    }

    private sealed class ReprojectFilter(MathTransform transform) : ICoordinateSequenceFilter
    {
        public bool Done => false;
        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence seq, int i)
        {
            var (x, y) = transform.Transform(seq.GetX(i), seq.GetY(i));
            seq.SetOrdinate(i, Ordinate.X, x);
            seq.SetOrdinate(i, Ordinate.Y, y);
        }
    }
}
