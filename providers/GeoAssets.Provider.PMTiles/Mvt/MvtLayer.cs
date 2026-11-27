namespace GeoAssets.Provider.PMTiles.Mvt;

/// <summary>One decoded vector tile layer: its name, its extent (tile units per side) and its features.</summary>
internal sealed record MvtLayer(string Name, int Extent, IReadOnlyList<MvtFeature> Features);
