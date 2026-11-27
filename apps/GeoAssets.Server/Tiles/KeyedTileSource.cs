using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>One deployment-wide tile source the kernel serves: its key, its format and the service itself.</summary>
/// <param name="Id">The key it is registered under — the <c>{id}</c> of its URLs.</param>
/// <param name="Format">Its tiles' format: <see cref="TileFormat.Mvt"/> for an <see cref="IVectorTileSource"/>, <see cref="TileFormat.Png"/> for an <see cref="IRasterTileSource"/>.</param>
/// <param name="Source">The <see cref="IVectorTileSource"/> or <see cref="IRasterTileSource"/>.</param>
/// <param name="Described">The same source, describing itself.</param>
public sealed record KeyedTileSource(string Id, TileFormat Format, object Source, IDescribedTileSource Described)
{
    /// <summary>The tile at <paramref name="request"/>'s address, as the source draws it.</summary>
    public Task<TilePayload> GetTileAsync(TileRequest request, CancellationToken ct) =>
        Format == TileFormat.Mvt
            ? ((IVectorTileSource)Source).GetVectorTileAsync(request, ct)
            : ((IRasterTileSource)Source).GetRasterTileAsync(request, ct);
}
