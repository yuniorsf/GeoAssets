using System.Globalization;
using System.Runtime.InteropServices;
using GeoAssets.Core.Rendering;
using OSGeo.GDAL;

namespace GeoAssets.Provider.Gdal.Cog;

/// <summary>
/// <see cref="IRasterTileSource"/> over a Cloud Optimized GeoTIFF (ADR-002 D5/D6, XD01-437): each tile is the raster
/// warped by GDAL (<c>gdalwarp</c>, bilinear) onto the tile's EPSG:3857 envelope at 256 × 256 pixels, with an alpha
/// band (transparent outside the raster and over its nodata), encoded as PNG — GDAL reads only the overview and blocks
/// the tile needs. A raster that isn't 8-bit is stretched to 0–255 over its bands' minimum and maximum (computed once,
/// approximately); a paletted one is expanded to RGBA first.
/// <para>
/// The source is one layer: a request whose layer set doesn't name <see cref="LayerId"/>, or a tile the raster doesn't
/// reach, is <see cref="TilePayload.Empty"/>. The raster is opened per request (a GDAL dataset is not thread-safe), so
/// concurrent tiles never share one. <see cref="Path"/> may be any path GDAL opens — a local file or a <c>/vsicurl/</c>
/// URL.
/// </para>
/// </summary>
public sealed class CogRasterTileSource : IRasterTileSource
{
    /// <summary>A tile's width and height in pixels.</summary>
    public const int TileSize = 256;

    private readonly Lazy<double[][]?> _byteScale;

    /// <summary>A source serving the raster at <paramref name="path"/> as layer <paramref name="layerId"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="layerId"/> breaks <see cref="TileKeySegment"/>, or <paramref name="path"/> is blank.</exception>
    public CogRasterTileSource(string layerId, string path)
    {
        TileKeySegment.EnsureValid(layerId, nameof(layerId));
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        LayerId = layerId;
        Path = path;
        _byteScale = new Lazy<double[][]?>(ReadByteScale, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The layer id this source answers for.</summary>
    public string LayerId { get; }

    /// <summary>The raster GDAL opens.</summary>
    public string Path { get; }

    /// <inheritdoc />
    /// <exception cref="GdalNativeUnavailableException">Native GDAL can't run here.</exception>
    /// <exception cref="FileNotFoundException">There is no raster at <see cref="Path"/>.</exception>
    /// <exception cref="InvalidDataException">GDAL can't read or warp the raster.</exception>
    public Task<TilePayload> GetRasterTileAsync(TileRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();

        if (!request.LayerSet.LayerIds.Contains(LayerId, StringComparer.Ordinal))
            return Task.FromResult(TilePayload.Empty(TileFormats.PngContentType));

        GdalNativeInitializer.EnsureInitialized();
        try
        {
            return Task.FromResult(Render(request.Address, ct));
        }
        catch (ApplicationException ex)
        {
            throw new InvalidDataException($"GDAL can't render tile {request.Address} of raster '{Path}': {ex.Message}", ex);
        }
    }

    private TilePayload Render(TileAddress address, CancellationToken ct)
    {
        using var source = OpenSource();
        using var prepared = PrepareBytes(source);
        ct.ThrowIfCancellationRequested();

        var envelope = address.Envelope3857;
        string[] options =
        [
            "-of", "MEM",
            "-t_srs", "EPSG:3857",
            "-te", Invariant(envelope.MinX), Invariant(envelope.MinY), Invariant(envelope.MaxX), Invariant(envelope.MaxY),
            "-ts", Invariant(TileSize), Invariant(TileSize),
            "-r", "bilinear",
            "-dstalpha",
        ];
        using var warpOptions = new GDALWarpAppOptions(options);
        using var warped = OSGeo.GDAL.Gdal.Warp(string.Empty, [prepared ?? source], warpOptions, null, null)
            ?? throw Failure("warp");
        ct.ThrowIfCancellationRequested();

        return IsTransparent(warped)
            ? TilePayload.Empty(TileFormats.PngContentType)
            : new TilePayload(EncodePng(warped), TileFormats.PngContentType);
    }

    private Dataset OpenSource()
    {
        if (!Path.StartsWith("/vsi", StringComparison.Ordinal) && !File.Exists(Path))
            throw new FileNotFoundException($"Raster '{Path}' does not exist.", Path);

        return OSGeo.GDAL.Gdal.Open(Path, Access.GA_ReadOnly) ?? throw Failure("open");
    }

    /// <summary>An 8-bit (or RGBA) view of the raster when it isn't one already; <see langword="null"/> when it is.</summary>
    private Dataset? PrepareBytes(Dataset source)
    {
        using var first = source.GetRasterBand(1);
        using var palette = first.GetRasterColorTable();
        string[] options;
        if (palette is not null)
        {
            options = ["-of", "VRT", "-expand", "rgba"];
        }
        else if (_byteScale.Value is { } scale)
        {
            var list = new List<string> { "-of", "VRT", "-ot", "Byte" };
            for (var band = 0; band < scale.Length; band++)
                list.AddRange([$"-scale_{(band + 1).ToString(CultureInfo.InvariantCulture)}", Invariant(scale[band][0]), Invariant(scale[band][1]), "0", "255"]);
            options = [.. list];
        }
        else
        {
            return null;
        }

        using var translateOptions = new GDALTranslateOptions(options);
        return OSGeo.GDAL.Gdal.wrapper_GDALTranslate(string.Empty, source, translateOptions, null, null) ?? throw Failure("prepare");
    }

    /// <summary>Each band's (minimum, maximum) when the raster isn't 8-bit, else <see langword="null"/>.</summary>
    private double[][]? ReadByteScale()
    {
        using var source = OpenSource();
        var scale = new double[source.RasterCount][];
        var allBytes = true;
        for (var band = 1; band <= source.RasterCount; band++)
        {
            using var raster = source.GetRasterBand(band);
            allBytes &= raster.DataType == DataType.GDT_Byte;
            var minMax = new double[2];
            raster.ComputeRasterMinMax(minMax, 1);
            scale[band - 1] = minMax[1] > minMax[0] ? minMax : [minMax[0], minMax[0] + 1];
        }

        return allBytes ? null : scale;
    }

    private static bool IsTransparent(Dataset warped)
    {
        using var alpha = warped.GetRasterBand(warped.RasterCount);
        var buffer = new byte[TileSize * TileSize];
        if (alpha.ReadRaster(0, 0, TileSize, TileSize, buffer, TileSize, TileSize, 0, 0) != CPLErr.CE_None)
            throw Failure("read");
        return !buffer.AsSpan().ContainsAnyExcept((byte)0);
    }

    private static byte[] EncodePng(Dataset warped)
    {
        var path = $"/vsimem/geoassets-cog-{Guid.NewGuid():N}.png";
        using var driver = OSGeo.GDAL.Gdal.GetDriverByName("PNG") ?? throw Failure("find the PNG driver for");
        try
        {
            using (var png = driver.CreateCopy(path, warped, 0, [], null, null) ?? throw Failure("encode"))
            {
                png.FlushCache();
            }

            var pointer = OSGeo.GDAL.Gdal.GetMemFileBuffer(path, out var length, false);
            if (pointer == IntPtr.Zero || length == 0 || length > int.MaxValue)
                throw Failure("encode");

            var bytes = new byte[(int)length];
            Marshal.Copy(pointer, bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            _ = OSGeo.GDAL.Gdal.Unlink(path);
        }
    }

    private static InvalidDataException Failure(string step) =>
        new($"GDAL failed to {step} the raster tile: {OSGeo.GDAL.Gdal.GetLastErrorMsg()}");

    private static string Invariant(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);
}
