namespace GeoAssets.Provider.PMTiles.Packs;

/// <summary>What an offline map pack holds (XD01-465), and so how the map draws it.</summary>
public enum PMTilesPackKind
{
    /// <summary>Vector tiles (MVT), drawn as features (<see cref="PMTilesPackAssetProvider"/>).</summary>
    Vector = 0,

    /// <summary>PNG, JPEG or WebP images, drawn as a basemap tile layer (<see cref="PMTilesRasterPackAssetProvider"/>).</summary>
    Raster = 1,
}
