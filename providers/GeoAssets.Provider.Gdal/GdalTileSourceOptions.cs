using GeoAssets.Core.Rendering;

namespace GeoAssets.Provider.Gdal;

/// <summary>One configured tile source: its id (the DI key and the layer id) and its file.</summary>
/// <param name="Id">The source's id — a <see cref="TileKeySegment"/>.</param>
/// <param name="Path">The file (absolute, or relative to the host's base directory), or for a raster any GDAL path such as <c>/vsicurl/https://…</c>.</param>
public sealed record GdalTileSourceOptions(string Id, string Path);
