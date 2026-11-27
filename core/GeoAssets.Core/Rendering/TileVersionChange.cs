namespace GeoAssets.Core.Rendering;

/// <summary>
/// One change of the shared tile versions, as an <see cref="ITileVersionStore"/> announces it (XD01-458): who made it
/// (<see cref="Origin"/>), the new <see cref="Generations"/> of the scopes it advanced, the new <see cref="Flushes"/> when
/// it flushed (<c>null</c> otherwise) and the write's dirty <see cref="Regions"/>. A change the announcement could not carry
/// whole (too large, or unreadable) is not <see cref="IsComplete"/>: a listener re-reads the versions and treats every
/// tile as dirty.
/// </summary>
public sealed record TileVersionChange(
    string Origin,
    IReadOnlyDictionary<Guid, long> Generations,
    long? Flushes,
    IReadOnlyList<DirtyTileRegion> Regions,
    bool IsComplete = true);
