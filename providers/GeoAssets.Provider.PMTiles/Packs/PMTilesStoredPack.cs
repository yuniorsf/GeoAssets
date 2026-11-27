namespace GeoAssets.Provider.PMTiles.Packs;

/// <summary>
/// A pack kept in a <see cref="PMTilesPackStorage"/> (XD01-464), as <see cref="PMTilesPackStorage.ListAsync"/> finds it —
/// read from the file's 127-byte header, without opening the archive.
/// </summary>
/// <param name="Id">The pack's id (its file is <c>&lt;id&gt;.pmtiles</c>).</param>
/// <param name="Path">The stored file.</param>
/// <param name="FileName">The name of the file it was imported from, when the import recorded it.</param>
/// <param name="SizeBytes">The stored file's size.</param>
/// <param name="ImportedAt">When it was imported: as recorded by the import, else the stored file's last write.</param>
/// <param name="Header">The file's header; <see langword="null"/> when it can't be read (a damaged file, still deletable).</param>
public sealed record PMTilesStoredPack(
    string Id,
    string Path,
    string? FileName,
    long SizeBytes,
    DateTimeOffset ImportedAt,
    PMTilesHeader? Header)
{
    /// <summary>What the pack holds, from <see cref="Header"/> (as <see cref="PMTilesPack.Kind"/>); <see langword="null"/> without one.</summary>
    public PMTilesPackKind? Kind => Header is null
        ? null
        : PMTilesRasterImages.IsSupported(Header.TileType) ? PMTilesPackKind.Raster : PMTilesPackKind.Vector;
}
