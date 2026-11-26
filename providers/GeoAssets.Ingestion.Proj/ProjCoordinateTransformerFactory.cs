using GeoAssets.Ingestion.Spatial;
using OSGeo.OSR;

namespace GeoAssets.Ingestion.Proj;

/// <summary>
/// Builds PROJ-backed (GDAL/OSR, <c>MaxRev.Gdal.Core</c>) transformers from a source CRS given as ESRI or OGC WKT to
/// SIRGAS 2000 geographic (EPSG:4674, x = lon, y = lat), output SRID 4326. Supported source datums: SAD69 —
/// <c>grid</c> when <c>br_ibge_SAD69_003.tif</c> is in a PROJ search path (<see cref="ProjOptions.ProjDataDirectory"/>
/// first), else the EPSG:15485 Helmert (<c>helmert</c>) — and SIRGAS 2000 / WGS 84 (<c>none</c>, no datum shift).
/// </summary>
public sealed class ProjCoordinateTransformerFactory(ProjOptions options) : ICoordinateTransformerFactory
{
    private const int SirgasEpsg = 4674;

    private readonly ProjOptions _options = options ?? throw new ArgumentNullException(nameof(options));

    public ICoordinateTransformer Create(string sourceCrsWkt)
    {
        if (string.IsNullOrWhiteSpace(sourceCrsWkt))
            throw new NotSupportedException("The source CRS is empty.");

        var searchPaths = GdalEnvironment.EnsureConfigured(_options.ProjDataDirectory);
        try
        {
            using var source = ParseSource(sourceCrsWkt);
            var datum = SourceDatums.Identify(source)
                ?? throw new NotSupportedException($"Unsupported source datum '{source.GetAttrValue("DATUM", 0)}' (supported: SAD69, SIRGAS 2000, WGS 84).");

            if (datum != SourceDatum.Sad69)
            {
                using var target = new SpatialReference(null);
                target.ImportFromEPSG(SirgasEpsg);
                target.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
                return new ProjCoordinateTransformer(source, target, PositionAccuracy.None, null, null);
            }

            using var geographic = source.CloneGeogCS();
            geographic.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
            var grid = DatumShifts.FindSad69Grid(GridSearchPaths(searchPaths));
            return grid is null
                ? new ProjCoordinateTransformer(source, geographic, PositionAccuracy.Helmert, DatumShifts.Sad69Helmert, null)
                : new ProjCoordinateTransformer(source, geographic, PositionAccuracy.Grid, DatumShifts.Sad69Grid(grid), DatumShifts.Sad69Helmert);
        }
        catch (ApplicationException exception)
        {
            throw new NotSupportedException($"PROJ cannot interpret the source CRS: {exception.Message}", exception);
        }
    }

    private IEnumerable<string> GridSearchPaths(IEnumerable<string> searchPaths) =>
        string.IsNullOrWhiteSpace(_options.ProjDataDirectory) ? searchPaths : searchPaths.Prepend(Path.GetFullPath(_options.ProjDataDirectory));

    private static SpatialReference ParseSource(string wkt)
    {
        var source = new SpatialReference(null);
        try
        {
            var text = wkt.Trim();
            if (source.ImportFromWkt(ref text) != 0)
                throw new NotSupportedException("The source CRS WKT cannot be parsed.");
            source.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
            return source;
        }
        catch
        {
            source.Dispose();
            throw;
        }
    }
}
