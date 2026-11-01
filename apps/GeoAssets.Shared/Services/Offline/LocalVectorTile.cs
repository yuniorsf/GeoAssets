namespace GeoAssets.Shared.Services.Offline;

/// <summary>A single cached map tile, keyed by the standard slippy-map Z/X/Y scheme (XD01-243).</summary>
public sealed record LocalVectorTile(int Z, int X, int Y, byte[] Data, DateTime CachedAtUtc);
