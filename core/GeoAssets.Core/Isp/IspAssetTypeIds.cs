namespace GeoAssets.Core.Isp;

/// <summary>Deterministic IDs for the ISP outside-plant layer <see cref="GeoAssets.Core.Models.AssetType"/>s (XD01-272).</summary>
public static class IspAssetTypeIds
{
    public static readonly Guid Cto = Guid.Parse("00000000-0000-0000-0000-00000000000c");
    public static readonly Guid Ceo = Guid.Parse("00000000-0000-0000-0000-00000000000d");
    public static readonly Guid OpticalCable = Guid.Parse("00000000-0000-0000-0000-00000000000e");
    public static readonly Guid Pto = Guid.Parse("00000000-0000-0000-0000-00000000000f");
    public static readonly Guid Ont = Guid.Parse("00000000-0000-0000-0000-000000000010");
}
