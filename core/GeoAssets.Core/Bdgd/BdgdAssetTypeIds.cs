namespace GeoAssets.Core.Bdgd;

/// <summary>Deterministic IDs for the ANEEL BDGD layer <see cref="AssetType"/>s (XD01-224).</summary>
public static class BdgdAssetTypeIds
{
    public static readonly Guid Ponnot = Guid.Parse("00000000-0000-0000-0000-0000000000b1");
    public static readonly Guid Untrd = Guid.Parse("00000000-0000-0000-0000-0000000000b2");
    public static readonly Guid Unremt = Guid.Parse("00000000-0000-0000-0000-0000000000b3");
    public static readonly Guid Segcon = Guid.Parse("00000000-0000-0000-0000-0000000000b4");
    public static readonly Guid Uc = Guid.Parse("00000000-0000-0000-0000-0000000000b5");
}
