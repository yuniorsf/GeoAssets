using GeoAssets.Core.Rendering;

namespace GeoAssets.Server.Tiles;

/// <summary>A deployment-wide keyed tile source as <c>style.json</c> lists it (XD01-459): its id, its tiles' format and what it holds now.</summary>
/// <param name="Id">The key it is registered under — the <c>{id}</c> of its URLs.</param>
/// <param name="Format">Its tiles' format.</param>
/// <param name="Description">What it holds: its <c>{v}</c>, layers, zooms and bounds.</param>
public sealed record PublishedTileSource(string Id, TileFormat Format, TileSourceDescription Description);
