using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using GeoAssets.Core.Models;
using GeoAssets.Core.VectorFiles;
using NetTopologySuite.IO;
using OSGeo.OGR;
using OSGeo.OSR;
using GeoGeometry = GeoAssets.Core.Models.Geometry.GeoGeometry;
using OgrLayer = OSGeo.OGR.Layer;

namespace GeoAssets.Provider.Gdal.VectorFiles;

/// <summary>
/// <see cref="IVectorTileArchiveWriter"/> through OGR's <c>PMTiles</c> driver (XD01-472), which tiles with GDAL's MVT
/// writer: every feature is cut into the tiles it touches at every zoom of the request as it is written (into a
/// temporary MBTiles file next to the archive, <c>&lt;archive&gt;.tmp.mbtiles</c>), and the PMTiles v3 archive — gzip
/// tiles, its metadata's <c>vector_layers</c> naming every layer — is assembled when the dataset closes. The dataset is
/// created with the first feature that has a geometry, so an input with nothing to tile never reaches the driver (which
/// can't close an archive without tiles).
/// <para>
/// One OGR layer per MVT layer, created when its first feature arrives, in EPSG:4326 (longitude, latitude), with the
/// text fields <c>id</c>, <c>name</c> and then the attributes — added as they first appear (the MVT writer takes new
/// fields between features), or all up front when the request declares them. Each feature's OGR FID is its 1-based
/// position in the input, and the MVT writer writes the FID as the MVT feature id, so every piece of a feature carries
/// the same id in every tile. Features are written one at a time as the input yields them; memory never holds more
/// than one. The archive is built under a temporary name and moved into place (or copied onto the stream) only once
/// complete; on failure or cancellation the temporary archive and the driver's MBTiles file are deleted.
/// </para>
/// </summary>
public sealed class OgrPMTilesArchiveWriter : IVectorTileArchiveWriter
{
    /// <summary>The OGR driver that writes PMTiles archives.</summary>
    public const string DriverName = "PMTiles";

    /// <summary>The file extension of a PMTiles archive.</summary>
    public const string Extension = ".pmtiles";

    private const string IdTag = "id";
    private const string NameTag = "name";
    private const string StreamArchiveName = "features";

    /// <inheritdoc />
    public bool IsAvailable
    {
        get
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

            using var driver = Ogr.GetDriverByName(DriverName);
            return driver is not null && CanCreate();
        }
    }

    /// <inheritdoc />
    public async Task<VectorTileArchiveWriteResult> WriteAsync(
        string path, VectorTileArchiveWriteRequest request, IAsyncEnumerable<GeoFeature> features, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(features);
        ct.ThrowIfCancellationRequested();

        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"The folder of '{target}' does not exist.");

        using var driver = Driver();
        var temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}{Extension}");
        try
        {
            var result = await WriteArchiveAsync(driver, temporary, Path.GetFileNameWithoutExtension(target), request, features, ct).ConfigureAwait(false);
            File.Move(temporary, target, overwrite: true);
            return result;
        }
        finally
        {
            DeleteTemporaries(directory, Path.GetFileName(temporary));
        }
    }

    /// <inheritdoc />
    public async Task<VectorTileArchiveWriteResult> WriteAsync(
        Stream destination, VectorTileArchiveWriteRequest request, IAsyncEnumerable<GeoFeature> features, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(features);
        if (!destination.CanWrite)
            throw new ArgumentException("The destination stream is not writable.", nameof(destination));
        ct.ThrowIfCancellationRequested();

        using var driver = Driver();
        var directory = Path.Combine(Path.GetTempPath(), "geoassets-pmtiles-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, StreamArchiveName + Extension);
            var result = await WriteArchiveAsync(driver, file, StreamArchiveName, request, features, ct).ConfigureAwait(false);
            var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (source.ConfigureAwait(false))
                await source.CopyToAsync(destination, ct).ConfigureAwait(false);
            return result;
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static async Task<VectorTileArchiveWriteResult> WriteArchiveAsync(
        Driver driver, string file, string archiveName, VectorTileArchiveWriteRequest request, IAsyncEnumerable<GeoFeature> features, CancellationToken ct)
    {
        long written = 0;
        long skipped = 0;
        long position = 0;
        var layers = new List<TileLayer>();
        DataSource? dataSource = null;
        try
        {
            using var wgs84 = Wgs84();
            var byName = new Dictionary<string, TileLayer>(StringComparer.Ordinal);
            var wkb = new WKBWriter();
            await foreach (var feature in features.WithCancellation(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(feature, nameof(features));
                position++;
                if (feature.Geometry is not { IsEmpty: false } geometry)
                {
                    skipped++;
                    continue;
                }

                dataSource ??= driver.CreateDataSource(file, DatasetOptions(archiveName, request))
                    ?? throw new InvalidDataException($"GDAL can't create the PMTiles archive '{file}': {OSGeo.GDAL.Gdal.GetLastErrorMsg()}");
                var layerName = request.LayerNameOf(feature.Properties.AssetTypeId);
                if (!byName.TryGetValue(layerName, out var layer))
                {
                    layer = TileLayer.Create(dataSource, layerName, wgs84, request.Attributes);
                    layers.Add(layer);
                    byName.Add(layerName, layer);
                }

                layer.Write(feature, geometry, position, request.Attributes is null, wkb);
                written++;
            }
        }
        catch (Exception ex)
        {
            Close(layers, dataSource, out _);
            if (ex is ApplicationException)
                throw new InvalidDataException($"GDAL can't write the PMTiles archive: {ex.Message}", ex);
            throw;
        }

        if (written == 0)
        {
            throw new InvalidDataException(skipped == 0
                ? "There are no features to tile: a PMTiles archive holds at least one."
                : $"None of the {skipped.ToString(CultureInfo.InvariantCulture)} features has a geometry to tile: a PMTiles archive holds at least one.");
        }

        if (!Close(layers, dataSource, out var closeError))
            throw new InvalidDataException($"GDAL can't finish the PMTiles archive: {closeError.Message}", closeError);
        if (!File.Exists(file))
            throw new InvalidDataException($"GDAL wrote no PMTiles archive: {OSGeo.GDAL.Gdal.GetLastErrorMsg()}");

        return new VectorTileArchiveWriteResult(written, skipped, [.. layers.Select(l => l.Name)], request.MinZoom, request.MaxZoom);
    }

    /// <summary>
    /// Closes the archive — the driver assembles it now — and answers whether that worked. An error the close raises is
    /// reported by the OGR bindings on their next call on this thread, not by the close itself, so it is drawn out here
    /// (into <paramref name="error"/>) rather than left to fail whatever GDAL call comes next.
    /// </summary>
    private static bool Close(List<TileLayer> layers, DataSource? dataSource, [NotNullWhen(false)] out ApplicationException? error)
    {
        foreach (var layer in layers)
            layer.Dispose();
        error = null;
        if (dataSource is null)
            return true;

        dataSource.Dispose();
        try
        {
            _ = Ogr.GetDriverCount();
            return true;
        }
        catch (ApplicationException ex)
        {
            error = ex;
            return false;
        }
    }

    private static string[] DatasetOptions(string archiveName, VectorTileArchiveWriteRequest request)
    {
        List<string> options =
        [
            "NAME=" + archiveName,
            Option("MINZOOM", request.MinZoom),
            Option("MAXZOOM", request.MaxZoom),
            Option("EXTENT", request.Extent),
            Option("BUFFER", request.Buffer),
        ];
        if (request.Simplification is { } simplification)
            options.Add("SIMPLIFICATION=" + simplification.ToString("R", CultureInfo.InvariantCulture));
        if (request.SimplificationMaxZoom is { } simplificationMaxZoom)
            options.Add("SIMPLIFICATION_MAX_ZOOM=" + simplificationMaxZoom.ToString("R", CultureInfo.InvariantCulture));
        return [.. options];
    }

    private static string Option(string name, int value) => name + "=" + value.ToString(CultureInfo.InvariantCulture);

    private static SpatialReference Wgs84()
    {
        var reference = new SpatialReference(null);
        try
        {
            _ = reference.ImportFromEPSG(4326);
            reference.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
            return reference;
        }
        catch
        {
            reference.Dispose();
            throw;
        }
    }

    private static Driver Driver()
    {
        if (!GdalNativeRuntime.IsSupportedPlatform)
            throw new VectorTileArchiveUnavailableException($"PMTiles archives can't be written here: the plugin has no native GDAL runtime for {GdalNativeRuntime.Describe()}.");

        try
        {
            GdalNativeInitializer.EnsureInitialized();
        }
        catch (GdalNativeUnavailableException ex)
        {
            throw new VectorTileArchiveUnavailableException($"PMTiles archives can't be written here: {ex.Message}", ex);
        }

        var driver = Ogr.GetDriverByName(DriverName)
            ?? throw new VectorTileArchiveUnavailableException(
                $"PMTiles archives can't be written here: GDAL {GdalNativeInitializer.NativeVersion} was built without its '{DriverName}' driver.");
        if (CanCreate())
            return driver;

        driver.Dispose();
        throw new VectorTileArchiveUnavailableException(
            $"PMTiles archives can't be written here: GDAL {GdalNativeInitializer.NativeVersion}'s '{DriverName}' driver can't create files.");
    }

    private static bool CanCreate()
    {
        using var driver = OSGeo.GDAL.Gdal.GetDriverByName(DriverName);
        return driver is not null && string.Equals(driver.GetMetadataItem("DCAP_CREATE", string.Empty), "YES", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Deletes <paramref name="temporaryName"/> and every file the driver named after it (its MBTiles file, SQLite journals).</summary>
    private static void DeleteTemporaries(string directory, string temporaryName)
    {
        foreach (var file in Directory.EnumerateFiles(directory, temporaryName + "*"))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// One MVT layer's OGR layer and its fields: <c>id</c>, <c>name</c>, then the attributes by first appearance. A key
    /// spelled <c>id</c> or <c>name</c> (any case) is left to those two; two keys equal but for case can't both be
    /// fields (OGR looks fields up ignoring case).
    /// </summary>
    private sealed class TileLayer : IDisposable
    {
        private readonly OgrLayer _layer;
        private readonly Dictionary<string, int> _fields = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _seen = new(StringComparer.OrdinalIgnoreCase)
        {
            [IdTag] = IdTag,
            [NameTag] = NameTag,
        };

        private FeatureDefn _definition;

        private TileLayer(string name, OgrLayer layer)
        {
            Name = name;
            _layer = layer;
            AddField(IdTag);
            AddField(NameTag);
            _definition = layer.GetLayerDefn();
        }

        public string Name { get; }

        public static TileLayer Create(DataSource dataSource, string name, SpatialReference wgs84, IReadOnlyList<string>? declared)
        {
            var ogrLayer = dataSource.CreateLayer(name, wgs84, wkbGeometryType.wkbUnknown, [])
                ?? throw new InvalidDataException($"GDAL can't create the layer '{name}': {OSGeo.GDAL.Gdal.GetLastErrorMsg()}");
            var layer = new TileLayer(name, ogrLayer);
            try
            {
                if (declared is not null)
                {
                    foreach (var attribute in declared)
                        layer.EnsureAttribute(attribute);
                }

                return layer;
            }
            catch
            {
                layer.Dispose();
                throw;
            }
        }

        public void Write(GeoFeature feature, GeoGeometry geometry, long fid, bool addAttributes, WKBWriter wkb)
        {
            var attributes = feature.Properties.CustomAttributes;
            if (addAttributes)
            {
                foreach (var key in attributes.Keys)
                    EnsureAttribute(key);
            }

            using var row = new Feature(_definition);
            row.SetFID(fid);
            row.SetField(0, feature.Id);
            row.SetField(1, feature.Properties.Name);
            foreach (var (key, value) in attributes)
            {
                if (value is not null && _fields.TryGetValue(key, out var index))
                    row.SetField(index, value);
            }

            using var ogrGeometry = Geometry.CreateFromWkb(wkb.Write(geometry.NtsGeometry));
            row.SetGeometry(ogrGeometry);
            if (_layer.CreateFeature(row) != 0)
                throw new InvalidDataException($"GDAL can't write feature '{feature.Id}': {OSGeo.GDAL.Gdal.GetLastErrorMsg()}");
        }

        public void Dispose()
        {
            _definition.Dispose();
            _layer.Dispose();
        }

        private void EnsureAttribute(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            if (_seen.TryGetValue(key, out var existing))
            {
                if (string.Equals(existing, key, StringComparison.Ordinal) || existing is IdTag or NameTag)
                    return;

                throw new InvalidDataException(
                    $"The attributes '{existing}' and '{key}' of layer '{Name}' differ only by case; a vector tile layer can't hold both as fields.");
            }

            _seen.Add(key, key);
            AddField(key);
            _fields.Add(key, _fields.Count + 2);
            _definition.Dispose();
            _definition = _layer.GetLayerDefn();
        }

        private void AddField(string name)
        {
            using var field = new FieldDefn(name, FieldType.OFTString);
            if (_layer.CreateField(field, 0) != 0)
                throw new InvalidDataException($"GDAL can't create the field '{name}' of layer '{Name}': {OSGeo.GDAL.Gdal.GetLastErrorMsg()}");
        }
    }
}
