using GeoAssets.Core.Models;

namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Converts a set of features into an ESRI Shapefile package zipped into a single archive.
/// A single .shp can only hold one geometry type, so features are split into up to three
/// Shapefiles (points / lines / polygons) inside the zip — see the implementation in
/// <c>GeoAssets.Provider.Shapefile</c> (same project as the Shapefile provider's reader, since
/// both share the NetTopologySuite.IO.ShapeFile dependency).
/// </summary>
public interface IShapefileExportService
{
    /// <summary>
    /// Builds a ZIP archive containing one Shapefile (.shp/.shx/.dbf/.prj) per distinct geometry
    /// category found among <paramref name="features"/>. Features with no geometry, or a
    /// geometry type that doesn't map to Point/LineString/Polygon (e.g. a mixed
    /// GeometryCollection), are skipped. Throws <see cref="InvalidOperationException"/> if no
    /// feature produced an exportable geometry.
    ///
    /// <paramref name="targetCrsWkt"/>/<paramref name="targetSrid"/> (XD01-284) default to WGS84
    /// (SRID 4326) exactly like before this pair of parameters existed — pass both together when
    /// <paramref name="features"/>' own geometries already carry coordinates in a different CRS
    /// (e.g. SIRGAS 2000 UTM) so the written <c>.prj</c> actually matches the coordinate values,
    /// rather than always claiming WGS84.
    /// </summary>
    byte[] ExportToZip(IReadOnlyList<GeoFeature> features, string? targetCrsWkt = null, int? targetSrid = null);
}
