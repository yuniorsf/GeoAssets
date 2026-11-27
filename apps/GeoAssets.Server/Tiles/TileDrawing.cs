using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>
/// What one drawing of a tile source produced (XD01-434): its tiles (one, or a raster metatile's block) and the
/// <see cref="TileVersions.Generation"/> it began at — a write invalidated after that may not be in them.
/// </summary>
public sealed record TileDrawing(IReadOnlyDictionary<TileAddress, TilePayload> Tiles, long Generation);
