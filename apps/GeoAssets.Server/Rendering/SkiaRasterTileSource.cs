using GeoAssets.Core.Rendering;
using SkiaSharp;

namespace GeoAssets.Server.Rendering;

/// <summary>
/// The SkiaSharp <see cref="IRasterTileSource"/> (ADR-002 D6, XD01-433): 256 × 256 PNG tiles of the EPSG:3857 pyramid,
/// drawn by <see cref="SkiaFeaturePainter"/> — the WMS GetMap painter — from the request's <see cref="IAssetRenderQuery"/>,
/// so every feature is in the color <see cref="RenderStyle"/> resolves through <see cref="Core.Services.LayerResolver"/>,
/// the same resolution the vector tiles' style evaluates.
///
/// <para><b>Metatiles.</b> A tile is cut from its <see cref="MetatileSpan"/> × <see cref="MetatileSpan"/> metatile
/// (<see cref="TileAddress.Metatile"/>, 2 × 2 by default): one read and one drawing for the whole block, which
/// <see cref="GetMetatileAsync"/> answers as every one of its tiles at once (so a cache can keep the neighbours of the tile
/// asked for). Neighbouring tiles of one metatile are slices of one image; across metatiles the
/// <see cref="SymbolBuffer"/> keeps them seamless: each metatile reads and draws <see cref="SymbolBuffer"/> pixels past
/// its edge, more than any symbol reaches past its geometry (<see cref="SkiaFeaturePainter.MaxSymbolOverhang"/>), so a
/// point, line or ring outline near or on a tile edge is drawn whole on both sides of it.</para>
///
/// <para><b>Layers.</b> A layer id is an asset type id, as for <c>PostgresVectorTileSource</c>: the layers are drawn bottom
/// first, one render query each (its <see cref="RenderQueryOptions.AssetTypeId"/>), at the tile's zoom
/// (<see cref="RenderQueryOptions.Zoom"/>, so an asset type whose own <c>MinZoom</c>/<c>MaxZoom</c> excludes it is not
/// drawn). A layer the <see cref="ILodPolicy"/> calls <see cref="TileRenderTier.Hidden"/> at that zoom is left out without
/// a read; any other tier is drawn as its features (a raster image already merges dense features into pixels).</para>
///
/// <para><b>Budget.</b> A metatile draws at most <see cref="MaxFeatures"/> features — the first by layer, then ordinal id
/// (<see cref="RenderQueryOptions.MaxCount"/> is what the earlier layers left), as <c>PostgresVectorTileSource</c> spends
/// its budget.</para>
///
/// <para><b>Empty tiles.</b> A tile with no pixel drawn is <see cref="TilePayload.Empty"/> of
/// <see cref="TileFormats.PngContentType"/> (as <see cref="IRasterTileSource"/> asks), not a transparent PNG: the
/// endpoint chooses how to answer it (e.g. <c>204 No Content</c>).</para>
///
/// <para><b>Cancellation.</b> A cancelled request throws <see cref="OperationCanceledException"/> — before the first read
/// when already cancelled, after a read otherwise — never an empty tile, which a cache would keep (a render query may
/// answer a cancelled read with no features, as <c>PostgresAssetRenderQuery</c> does).</para>
///
/// <para><b>Scope.</b> The render query's: the caller's ambient tenant scope. Scoped like it.</para>
/// </summary>
public sealed class SkiaRasterTileSource : IRasterTileSource, IRasterMetatileSource
{
    /// <summary>The width and height of a tile, in pixels.</summary>
    public const int TileSize = 256;

    /// <summary>How far, in pixels, a metatile is read and drawn past its edge — above <see cref="SkiaFeaturePainter.MaxSymbolOverhang"/> plus a pixel of anti-aliasing.</summary>
    public const int SymbolBuffer = 8;

    /// <summary>The default <see cref="MetatileSpan"/>: a 2 × 2 metatile.</summary>
    public const int DefaultMetatileSpan = 2;

    /// <summary>The default <see cref="MaxFeatures"/>: the render query's own bound.</summary>
    public const int DefaultMaxFeatures = RenderQueryOptions.DefaultMaxCount;

    private static readonly ILodPolicy NeverHidden = new LodPolicy(LodThresholds.VectorOnly);

    private readonly IAssetRenderQuery _renderQuery;
    private readonly ILodPolicy _lodPolicy;

    /// <param name="renderQuery">The features to draw, in the caller's tenant scope.</param>
    /// <param name="lodPolicy">Which layers are hidden at a zoom; <c>null</c> = none (only the asset types' own zoom ranges hide features).</param>
    /// <param name="maxFeatures">The most features one metatile draws (<see cref="MaxFeatures"/>).</param>
    /// <param name="metatileSpan">The tiles per side of a metatile (<see cref="MetatileSpan"/>); 1 draws every tile alone.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFeatures"/> or <paramref name="metatileSpan"/> is not positive.</exception>
    public SkiaRasterTileSource(
        IAssetRenderQuery renderQuery,
        ILodPolicy? lodPolicy,
        int maxFeatures = DefaultMaxFeatures,
        int metatileSpan = DefaultMetatileSpan)
    {
        ArgumentNullException.ThrowIfNull(renderQuery);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFeatures);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(metatileSpan);

        _renderQuery = renderQuery;
        _lodPolicy = lodPolicy ?? NeverHidden;
        MaxFeatures = maxFeatures;
        MetatileSpan = metatileSpan;
    }

    /// <summary>The most features one metatile draws.</summary>
    public int MaxFeatures { get; }

    /// <summary>The tiles per side of a metatile.</summary>
    public int MetatileSpan { get; }

    public async Task<TilePayload> GetRasterTileAsync(TileRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var metatile = await RenderAsync(request, ct).ConfigureAwait(false);
        return metatile?.Slice(request.Address) ?? Empty;
    }

    /// <summary>
    /// Every tile of the metatile holding <see cref="TileRequest.Address"/> (<see cref="TileAddress.Metatile"/> of
    /// <see cref="MetatileSpan"/>) from one read and one drawing — each the payload
    /// <see cref="GetRasterTileAsync"/> answers for it.
    /// </summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public async Task<IReadOnlyDictionary<TileAddress, TilePayload>> GetMetatileAsync(TileRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var metatile = await RenderAsync(request, ct).ConfigureAwait(false);
        return request.Address.Metatile(MetatileSpan)
            .EnumerateTiles()
            .ToDictionary(tile => tile, tile => metatile?.Slice(tile) ?? Empty);
    }

    private static TilePayload Empty => TilePayload.Empty(TileFormats.PngContentType);

    /// <summary>The drawn metatile of <paramref name="request"/>'s tile; <c>null</c> when it has nothing to draw.</summary>
    private async Task<MetatileImage?> RenderAsync(TileRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var zoom = request.Address.Z;
        var layers = request.LayerSet.LayerIds.Where(layer => _lodPolicy.Decide(layer, zoom) != TileRenderTier.Hidden).ToList();
        if (layers.Count == 0) return null;

        var range = request.Address.Metatile(MetatileSpan);
        var frame = MetatileFrame.Of(range);
        var features = new List<IReadOnlyList<RenderableFeature>>(layers.Count);
        var remaining = MaxFeatures;
        foreach (var layer in layers)
        {
            if (remaining <= 0) break;

            var options = new RenderQueryOptions { AssetTypeId = layer, Zoom = zoom, MaxCount = remaining };
            var rows = await _renderQuery.GetRenderableAsync(frame.ReadBox, options, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            features.Add(rows);
            remaining -= rows.Count;
        }

        return features.Exists(rows => rows.Count > 0) ? MetatileImage.Draw(frame, features) : null;
    }

    /// <summary>Where a metatile's image lies on the Web Mercator plane, symbol buffer included.</summary>
    private sealed record MetatileFrame(TileRange Range, PixelTransform Transform, BoundingBox ReadBox, int Width, int Height)
    {
        public static MetatileFrame Of(TileRange range)
        {
            var northWest = new TileAddress(range.Zoom, range.MinX, range.MinY).Envelope3857;
            var southEast = new TileAddress(range.Zoom, range.MaxX, range.MaxY).Envelope3857;
            var metersPerPixel = northWest.Width / TileSize;
            var buffer = SymbolBuffer * metersPerPixel;

            var originX = northWest.MinX - buffer;
            var originY = northWest.MaxY + buffer;
            var readBox = WebMercator.Unproject(new MercatorBounds(originX, southEast.MinY - buffer, southEast.MaxX + buffer, originY));
            return new MetatileFrame(
                range,
                PixelTransform.WebMercator(originX, originY, metersPerPixel),
                readBox,
                (range.Width * TileSize) + (2 * SymbolBuffer),
                (range.Height * TileSize) + (2 * SymbolBuffer));
        }

        /// <summary>The pixels of <paramref name="tile"/> in the metatile's image.</summary>
        public SKRectI PixelsOf(TileAddress tile)
        {
            var left = SymbolBuffer + ((tile.X - Range.MinX) * TileSize);
            var top = SymbolBuffer + ((tile.Y - Range.MinY) * TileSize);
            return new SKRectI(left, top, left + TileSize, top + TileSize);
        }
    }

    /// <summary>A drawn metatile, cut into PNG tiles on demand.</summary>
    private sealed class MetatileImage : IDisposable
    {
        private const int BytesPerPixel = 4;
        private const int AlphaOffset = 3;

        private readonly MetatileFrame _frame;
        private readonly SKBitmap _bitmap;
        private readonly SKImage _image;

        private MetatileImage(MetatileFrame frame, SKBitmap bitmap, SKImage image)
        {
            _frame = frame;
            _bitmap = bitmap;
            _image = image;
        }

        public static MetatileImage Draw(MetatileFrame frame, IReadOnlyList<IReadOnlyList<RenderableFeature>> layers)
        {
            var bitmap = new SKBitmap(frame.Width, frame.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            try
            {
                using (var canvas = new SKCanvas(bitmap))
                {
                    canvas.Clear(SKColors.Transparent);
                    foreach (var features in layers)
                        SkiaFeaturePainter.Paint(canvas, features, frame.Transform);
                }

                return new MetatileImage(frame, bitmap, SKImage.FromBitmap(bitmap));
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }

        public TilePayload Slice(TileAddress tile)
        {
            var pixels = _frame.PixelsOf(tile);
            if (!HasInk(pixels)) return Empty;

            using var subset = _image.Subset(pixels);
            using var data = subset.Encode(SKEncodedImageFormat.Png, 100);
            return new TilePayload(data.ToArray(), TileFormats.PngContentType);
        }

        public void Dispose()
        {
            _image.Dispose();
            _bitmap.Dispose();
        }

        private bool HasInk(SKRectI pixels)
        {
            var span = _bitmap.GetPixelSpan();
            var rowBytes = _bitmap.RowBytes;
            for (var y = pixels.Top; y < pixels.Bottom; y++)
            {
                var row = span.Slice((y * rowBytes) + (pixels.Left * BytesPerPixel), pixels.Width * BytesPerPixel);
                for (var i = AlphaOffset; i < row.Length; i += BytesPerPixel)
                {
                    if (row[i] != 0) return true;
                }
            }

            return false;
        }
    }
}
