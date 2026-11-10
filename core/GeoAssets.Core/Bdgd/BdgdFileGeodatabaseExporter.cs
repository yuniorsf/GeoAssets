using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;

namespace GeoAssets.Core.Bdgd;

/// <summary>
/// Compiles completed asset mutations into an ANEEL-compliant ESRI File Geodatabase export
/// (XD01-225) — one FileGDB layer per BDGD entity type (<see cref="BdgdAssetTypes"/>), every
/// feature reprojected from this codebase's WGS84 storage CRS into SIRGAS 2000 UTM. Depends only
/// on <see cref="IFileGeodatabaseExportService"/> (the interface Core already owns), never the
/// concrete GDAL-backed provider, so Onion layering stays intact — same shape as
/// <see cref="GeoAssets.Core.Isp.Compliance.DisNor056Exporter"/>'s relationship to
/// <see cref="IShapefileExportService"/>.
///
/// All 5 BDGD layers are either Point (PONNOT/UNTRD/UNREMT/UC) or LineString (SEGCON) per
/// <see cref="BdgdAssetTypes"/> — there is no Polygon layer to reproject.
/// </summary>
public sealed class BdgdFileGeodatabaseExporter(IFileGeodatabaseExportService fileGeodatabaseExportService)
{
    private static readonly Dictionary<Guid, string> LayerNameByAssetTypeId = new()
    {
        [BdgdAssetTypeIds.Ponnot] = "PONNOT",
        [BdgdAssetTypeIds.Untrd] = "UNTRD",
        [BdgdAssetTypeIds.Unremt] = "UNREMT",
        [BdgdAssetTypeIds.Segcon] = "SEGCON",
        [BdgdAssetTypeIds.Uc] = "UC",
    };

    /// <summary>
    /// Builds the zipped .gdb package for every BDGD-typed feature in <paramref name="features"/>
    /// (any other feature is ignored). Throws <see cref="InvalidOperationException"/> if none
    /// qualify, or if no Point-geometry BDGD feature is present to anchor the UTM zone.
    /// </summary>
    public byte[] Export(IReadOnlyList<GeoFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        var bdgdFeatures = features
            .Where(f => Guid.TryParse(f.Properties.AssetTypeId, out var id) && LayerNameByAssetTypeId.ContainsKey(id))
            .ToList();
        if (bdgdFeatures.Count == 0)
        {
            throw new InvalidOperationException(
                "No BDGD-typed (PONNOT/UNTRD/UNREMT/SEGCON/UC) features found to export.");
        }

        // Same simplification DisNor056Exporter already makes: one UTM zone for the whole batch,
        // anchored on the first Point feature's longitude — a BDGD submission covers one
        // distributor's service area, which does not straddle UTM zones in practice.
        var anchorPoint = bdgdFeatures.Select(f => f.Geometry).OfType<GeoPoint>().FirstOrDefault()
            ?? throw new InvalidOperationException(
                "At least one Point-geometry BDGD feature is required to determine the SIRGAS 2000 UTM zone.");
        var zone = SirgasUtmZoneResolver.ZoneFor(anchorPoint.Longitude);
        var epsgCode = SirgasUtmZoneResolver.EpsgCodeFor(anchorPoint.Longitude);

        var layeredFeatures = bdgdFeatures
            .GroupBy(f => LayerNameByAssetTypeId[Guid.Parse(f.Properties.AssetTypeId!)], StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<GeoFeature>)group.Select(f => Reproject(f, zone, epsgCode)).ToList(),
                StringComparer.Ordinal);

        return fileGeodatabaseExportService.ExportToZip(layeredFeatures, SirgasUtmReprojector.WktFor(zone), epsgCode);
    }

    private static GeoFeature Reproject(GeoFeature feature, int zone, int epsgCode)
    {
        var geometry = feature.Geometry;
        GeoGeometry reprojected = geometry switch
        {
            GeoPoint point => ReprojectPoint(point, epsgCode),
            GeoLineString line => ReprojectLineString(line, zone, epsgCode),
            null => throw new InvalidOperationException($"Feature '{feature.Id}' has no geometry to export."),
            _ => throw new InvalidOperationException(
                $"Unsupported BDGD geometry type '{geometry.GetType().Name}' for feature '{feature.Id}'."),
        };

        return new GeoFeature { Id = feature.Id, Geometry = reprojected, Properties = feature.Properties };
    }

    private static GeoPoint ReprojectPoint(GeoPoint point, int epsgCode)
    {
        var (easting, northing, _) = SirgasUtmReprojector.ToSirgasUtm(point);
        return new GeoPoint(easting, northing, epsgCode);
    }

    private static GeoLineString ReprojectLineString(GeoLineString line, int zone, int epsgCode)
    {
        var reprojectedVertices = line.Coordinates
            .Select(c => SirgasUtmReprojector.TransformCoordinate(c[0], c[1], zone));
        return new GeoLineString(reprojectedVertices.Select(v => (v.Easting, v.Northing)), epsgCode);
    }
}
