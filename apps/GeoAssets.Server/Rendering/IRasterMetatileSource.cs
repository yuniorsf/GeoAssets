using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Rendering;

/// <summary>
/// An <see cref="IRasterTileSource"/> that draws a block of tiles at once (XD01-434) — <see cref="SkiaRasterTileSource"/>'s
/// metatile — so the tile endpoint caches every tile of one drawing instead of drawing the block again for each.
/// </summary>
public interface IRasterMetatileSource
{
    /// <summary>The tiles per side of a block (<see cref="TileAddress.Metatile"/>).</summary>
    int MetatileSpan { get; }

    /// <summary>Every tile of the block holding <see cref="TileRequest.Address"/>, each the payload the source answers for it alone.</summary>
    Task<IReadOnlyDictionary<TileAddress, TilePayload>> GetMetatileAsync(TileRequest request, CancellationToken ct);
}
