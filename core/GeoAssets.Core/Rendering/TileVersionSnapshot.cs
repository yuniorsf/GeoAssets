namespace GeoAssets.Core.Rendering;

/// <summary>
/// The shared tile versions as read from an <see cref="ITileVersionStore"/> (XD01-458): the <see cref="Epoch"/> (lowercase
/// hex digits), the <see cref="Flushes"/> so far and the <see cref="Generations"/> of the scopes asked for.
/// </summary>
public sealed record TileVersionSnapshot(string Epoch, long Flushes, IReadOnlyDictionary<Guid, long> Generations);
