using GeoAssets.Core.Models;
using GeoAssets.Core.VectorFiles;
using OSGeo.OGR;
using OSGeo.OSR;
using OgrLayer = OSGeo.OGR.Layer;

namespace GeoAssets.Provider.Gdal.VectorFiles;

/// <summary>
/// <see cref="IVectorFileReader"/> through GDAL/OGR (ADR-002 D5, XD01-437): FlatGeobuf with OGR's <c>FlatGeobuf</c>
/// driver, GeoParquet with its <c>Parquet</c> driver (both in the MaxRev Minimal runtime the plugin ships). A page is
/// one pass over at most <see cref="VectorFileReadRequest.Limit"/> features from <see cref="VectorFileReadRequest.Offset"/>
/// (<c>SetNextByIndex</c>), each feature's geometry reprojected to EPSG:4326 (longitude, latitude) when the layer has
/// another CRS, its fields copied into <see cref="GeoFeatureProperties.CustomAttributes"/> as text, its <c>name</c>
/// field (any case) into <see cref="GeoFeatureProperties.Name"/> and its <c>id</c> field — else its OGR FID — into
/// <see cref="GeoFeature.Id"/>. A format whose driver is missing throws <see cref="VectorFileFormatUnavailableException"/>
/// when read, never at startup.
/// </summary>
public sealed class OgrVectorFileReader : IVectorFileReader
{
    /// <summary>The OGR driver that reads <paramref name="format"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not defined.</exception>
    public static string DriverName(VectorFileFormat format) => format switch
    {
        VectorFileFormat.FlatGeobuf => "FlatGeobuf",
        VectorFileFormat.GeoParquet => "Parquet",
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unknown vector file format."),
    };

    /// <inheritdoc />
    public bool IsAvailable(VectorFileFormat format)
    {
        if (!GdalNativeRuntime.IsSupportedPlatform)
            return false;

        try
        {
            GdalNativeInitializer.EnsureInitialized();
        }
        catch (GdalNativeUnavailableException)
        {
            return false;
        }

        using var driver = Ogr.GetDriverByName(DriverName(format));
        return driver is not null;
    }

    /// <inheritdoc />
    public VectorFilePage ReadPage(VectorFileReadRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        using var driver = Driver(request.Format);
        if (!File.Exists(request.Path))
            throw new FileNotFoundException($"{request.Format} file '{request.Path}' does not exist.", request.Path);

        try
        {
            return Read(driver, request, ct);
        }
        catch (ApplicationException ex)
        {
            throw new InvalidDataException($"GDAL can't read {request.Format} file '{request.Path}': {ex.Message}", ex);
        }
    }

    private static VectorFilePage Read(Driver driver, VectorFileReadRequest request, CancellationToken ct)
    {
        using var dataSource = driver.Open(request.Path, 0)
            ?? throw new InvalidDataException($"'{request.Path}' is not a readable {request.Format} file: {OSGeo.GDAL.Gdal.GetLastErrorMsg()}");
        using var layer = Layer(dataSource, request);
        using var transform = ToWgs84(layer);

        var total = layer.GetFeatureCount(0);
        if (request.Offset > 0 && layer.SetNextByIndex(request.Offset) != 0)
            return new VectorFilePage([], null, total >= 0 ? total : null);

        var features = new List<GeoFeature>(Math.Min(request.Limit, 1024));
        Feature? next;
        while ((next = layer.GetNextFeature()) is not null)
        {
            using var feature = next;
            if (features.Count == request.Limit)
                return new VectorFilePage(features, request.Offset + features.Count, total >= 0 ? total : null);

            ct.ThrowIfCancellationRequested();
            features.Add(OgrFeatureMapper.ToGeoFeature(feature, transform));
        }

        return new VectorFilePage(features, null, total >= 0 ? total : null);
    }

    private static Driver Driver(VectorFileFormat format)
    {
        if (!GdalNativeRuntime.IsSupportedPlatform)
            throw new VectorFileFormatUnavailableException(format, $"the plugin has no native GDAL runtime for {GdalNativeRuntime.Describe()}.");

        try
        {
            GdalNativeInitializer.EnsureInitialized();
        }
        catch (GdalNativeUnavailableException ex)
        {
            throw new VectorFileFormatUnavailableException(format, ex.Message, ex);
        }

        return Ogr.GetDriverByName(DriverName(format))
            ?? throw new VectorFileFormatUnavailableException(
                format, $"GDAL {GdalNativeInitializer.NativeVersion} was built without its '{DriverName(format)}' driver.");
    }

    private static OgrLayer Layer(DataSource dataSource, VectorFileReadRequest request)
    {
        var layer = request.LayerName is null ? dataSource.GetLayerByIndex(0) : dataSource.GetLayerByName(request.LayerName);
        return layer ?? throw new InvalidDataException(request.LayerName is null
            ? $"'{request.Path}' has no layer."
            : $"'{request.Path}' has no layer '{request.LayerName}'.");
    }

    /// <summary>The transformation from the layer's CRS to EPSG:4326 (longitude, latitude); <see langword="null"/> when it is that already or has none.</summary>
    private static CoordinateTransformation? ToWgs84(OgrLayer layer)
    {
        using var layerReference = layer.GetSpatialRef();
        if (layerReference is null)
            return null;

        using var source = layerReference.Clone();

        using var target = new SpatialReference(null);
        _ = target.ImportFromEPSG(4326);
        target.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        source.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        return source.IsSame(target, []) == 1 ? null : new CoordinateTransformation(source, target);
    }
}
