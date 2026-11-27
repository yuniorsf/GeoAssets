using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Core.VectorFiles;
using NetTopologySuite.IO;
using OSGeo.OGR;
using OSGeo.OSR;
using OgrLayer = OSGeo.OGR.Layer;

namespace GeoAssets.Provider.Gdal.VectorFiles;

/// <summary>
/// <see cref="IVectorFileWriter"/> through GDAL/OGR (XD01-466) — the drivers <see cref="OgrVectorFileReader"/> reads
/// with: FlatGeobuf with OGR's <c>FlatGeobuf</c> driver (with its packed Hilbert R-tree, <c>SPATIAL_INDEX=YES</c>, so
/// every feature needs a geometry),
/// GeoParquet with its <c>Parquet</c> driver (one row group per <see cref="VectorFileWriteRequest.BatchSize"/> rows).
/// One layer named after the file, in EPSG:4326 (longitude, latitude), of any geometry type, with the text columns <c>id</c>, <c>name</c> and
/// then the attributes. Features are written one at a time as the input yields them; when the request names no
/// attributes, the input is first spooled to an NDJSON file next to the output to learn every attribute key (both
/// formats fix their columns before the first row), so memory never holds more than one feature. The output is built
/// under a temporary name and moved into place (or copied onto the stream) only once complete.
/// </summary>
public sealed class OgrVectorFileWriter : IVectorFileWriter
{
    private const string IdColumn = "id";
    private const string NameColumn = "name";
    private const int FirstAttributeField = 2;
    private const string StreamLayerName = "features";

    private static readonly byte[] NewLine = [(byte)'\n'];

    /// <summary>The file extension the format's driver expects (FlatGeobuf treats any other name as a folder of layers).</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="format"/> is not defined.</exception>
    public static string Extension(VectorFileFormat format) => format switch
    {
        VectorFileFormat.FlatGeobuf => ".fgb",
        VectorFileFormat.GeoParquet => ".parquet",
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

        using var driver = Ogr.GetDriverByName(OgrVectorFileReader.DriverName(format));
        return driver is not null && CanCreate(OgrVectorFileReader.DriverName(format));
    }

    /// <inheritdoc />
    public async Task<VectorFileWriteResult> WriteAsync(
        string path, VectorFileWriteRequest request, IAsyncEnumerable<GeoFeature> features, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(features);
        ct.ThrowIfCancellationRequested();

        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target)!;
        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException($"The folder of '{target}' does not exist.");

        using var driver = Driver(request.Format);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(target)}.{Guid.NewGuid():N}{Extension(request.Format)}");
        try
        {
            var result = await WriteFileAsync(driver, temporary, Path.GetFileNameWithoutExtension(target), request, features, ct).ConfigureAwait(false);
            File.Move(temporary, target, overwrite: true);
            return result;
        }
        finally
        {
            TryDelete(temporary);
        }
    }

    /// <inheritdoc />
    public async Task<VectorFileWriteResult> WriteAsync(
        Stream destination, VectorFileWriteRequest request, IAsyncEnumerable<GeoFeature> features, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(features);
        if (!destination.CanWrite)
            throw new ArgumentException("The destination stream is not writable.", nameof(destination));
        ct.ThrowIfCancellationRequested();

        using var driver = Driver(request.Format);
        var directory = Path.Combine(Path.GetTempPath(), "geoassets-vector-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var file = Path.Combine(directory, "export" + Extension(request.Format));
            var result = await WriteFileAsync(driver, file, StreamLayerName, request, features, ct).ConfigureAwait(false);
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

    private static async Task<VectorFileWriteResult> WriteFileAsync(
        Driver driver, string file, string layerName, VectorFileWriteRequest request, IAsyncEnumerable<GeoFeature> features, CancellationToken ct)
    {
        if (request.Attributes is { } declared)
        {
            var columns = new AttributeColumns();
            foreach (var attribute in declared)
                columns.Add(attribute);
            return await WriteRowsAsync(driver, file, layerName, request, columns.Names, features, ct).ConfigureAwait(false);
        }

        var spool = file + ".spool.ndjson";
        try
        {
            var columns = await SpoolAsync(features, spool, ct).ConfigureAwait(false);
            return await WriteRowsAsync(driver, file, layerName, request, columns, ReadSpoolAsync(spool, ct), ct).ConfigureAwait(false);
        }
        finally
        {
            TryDelete(spool);
        }
    }

    /// <summary>Copies the input to <paramref name="spool"/>, one compact GeoFeature per line, learning its attribute keys.</summary>
    private static async Task<IReadOnlyList<string>> SpoolAsync(IAsyncEnumerable<GeoFeature> features, string spool, CancellationToken ct)
    {
        var columns = new AttributeColumns();
        var stream = new FileStream(spool, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            await foreach (var feature in features.WithCancellation(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(feature, nameof(features));
                foreach (var key in feature.Properties.CustomAttributes.Keys)
                    columns.Add(key);
                await JsonSerializer.SerializeAsync(stream, feature, GeoJsonSerializer.CompactOptions, ct).ConfigureAwait(false);
                await stream.WriteAsync(NewLine, ct).ConfigureAwait(false);
            }
        }

        return columns.Names;
    }

    private static async IAsyncEnumerable<GeoFeature> ReadSpoolAsync(string spool, [EnumeratorCancellation] CancellationToken ct)
    {
        var stream = new FileStream(spool, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using (stream.ConfigureAwait(false))
        {
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
            {
                if (line.Length > 0)
                    yield return JsonSerializer.Deserialize<GeoFeature>(line, GeoJsonSerializer.CompactOptions)!;
            }
        }
    }

    private static async Task<VectorFileWriteResult> WriteRowsAsync(
        Driver driver,
        string file,
        string layerName,
        VectorFileWriteRequest request,
        IReadOnlyList<string> columns,
        IAsyncEnumerable<GeoFeature> features,
        CancellationToken ct)
    {
        long written = 0;
        try
        {
            using var dataSource = driver.CreateDataSource(file, [])
                ?? throw new InvalidDataException($"GDAL can't create the {request.Format} file '{file}': {OSGeo.GDAL.Gdal.GetLastErrorMsg()}");
            using var wgs84 = Wgs84();
            using var layer = dataSource.CreateLayer(layerName, wgs84, wkbGeometryType.wkbUnknown, LayerOptions(request))
                ?? throw new InvalidDataException($"GDAL can't create the layer '{layerName}': {OSGeo.GDAL.Gdal.GetLastErrorMsg()}");
            AddColumn(layer, IdColumn);
            AddColumn(layer, NameColumn);
            foreach (var column in columns)
                AddColumn(layer, column);

            using var definition = layer.GetLayerDefn();
            var wkb = new WKBWriter();
            await foreach (var feature in features.WithCancellation(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                ArgumentNullException.ThrowIfNull(feature, nameof(features));
                if (request.Format == VectorFileFormat.FlatGeobuf && feature.Geometry is not { IsEmpty: false })
                {
                    throw new InvalidDataException(
                        $"Feature '{feature.Id}' has no geometry: a FlatGeobuf file's spatial index needs one for every feature (GeoParquet holds features without one).");
                }

                using var row = Row(definition, feature, columns, wkb);
                if (layer.CreateFeature(row) != 0)
                    throw new InvalidDataException($"GDAL can't write feature '{feature.Id}': {OSGeo.GDAL.Gdal.GetLastErrorMsg()}");
                written++;
            }
        }
        catch (ApplicationException ex)
        {
            throw new InvalidDataException($"GDAL can't write the {request.Format} file: {ex.Message}", ex);
        }

        return new VectorFileWriteResult(written, columns);
    }

    private static string[] LayerOptions(VectorFileWriteRequest request) => request.Format switch
    {
        VectorFileFormat.FlatGeobuf => ["SPATIAL_INDEX=YES"],
        _ => [$"ROW_GROUP_SIZE={request.BatchSize.ToString(CultureInfo.InvariantCulture)}", "GEOMETRY_ENCODING=WKB"],
    };

    private static void AddColumn(OgrLayer layer, string name)
    {
        using var field = new FieldDefn(name, FieldType.OFTString);
        if (layer.CreateField(field, 0) != 0)
            throw new InvalidDataException($"GDAL can't create the column '{name}': {OSGeo.GDAL.Gdal.GetLastErrorMsg()}");
    }

    /// <summary>One OGR row for <paramref name="feature"/>, its dispose ownership passed to the caller (disposed here on failure).</summary>
    private static Feature Row(FeatureDefn definition, GeoFeature feature, IReadOnlyList<string> columns, WKBWriter wkb)
    {
        var row = new Feature(definition);
        try
        {
            row.SetField(0, feature.Id);
            row.SetField(1, feature.Properties.Name);
            var attributes = feature.Properties.CustomAttributes;
            for (var i = 0; i < columns.Count; i++)
            {
                if (attributes.TryGetValue(columns[i], out var value) && value is not null)
                    row.SetField(FirstAttributeField + i, value);
            }

            if (feature.Geometry is { } geometry && !geometry.IsEmpty)
            {
                using var ogrGeometry = Geometry.CreateFromWkb(wkb.Write(geometry.NtsGeometry));
                row.SetGeometry(ogrGeometry);
            }

            return row;
        }
        catch
        {
            row.Dispose();
            throw;
        }
    }

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

        var name = OgrVectorFileReader.DriverName(format);
        var driver = Ogr.GetDriverByName(name)
            ?? throw new VectorFileFormatUnavailableException(
                format, $"GDAL {GdalNativeInitializer.NativeVersion} was built without its '{name}' driver.");
        if (CanCreate(name))
            return driver;

        driver.Dispose();
        throw new VectorFileFormatUnavailableException(format, $"GDAL {GdalNativeInitializer.NativeVersion}'s '{name}' driver can't create files.");
    }

    private static bool CanCreate(string driverName)
    {
        using var driver = OSGeo.GDAL.Gdal.GetDriverByName(driverName);
        return driver is not null && string.Equals(driver.GetMetadataItem("DCAP_CREATE", string.Empty), "YES", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
    }

    /// <summary>
    /// The attribute columns in first-seen order. <c>id</c> and <c>name</c> (any case) are the feature's own columns, so
    /// such a key is skipped; two keys equal but for case can't both be columns (OGR looks fields up ignoring case).
    /// </summary>
    private sealed class AttributeColumns
    {
        private readonly List<string> _names = [];
        private readonly Dictionary<string, string> _seen = new(StringComparer.OrdinalIgnoreCase)
        {
            [IdColumn] = IdColumn,
            [NameColumn] = NameColumn,
        };

        public IReadOnlyList<string> Names => _names;

        public void Add(string key)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(key);
            if (_seen.TryGetValue(key, out var existing))
            {
                if (string.Equals(existing, key, StringComparison.Ordinal) || existing is IdColumn or NameColumn)
                    return;

                throw new InvalidDataException(
                    $"The attributes '{existing}' and '{key}' differ only by case; a vector file can't hold both as columns.");
            }

            _seen.Add(key, key);
            _names.Add(key);
        }
    }
}
