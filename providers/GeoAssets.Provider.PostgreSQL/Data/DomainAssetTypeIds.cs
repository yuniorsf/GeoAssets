namespace GeoAssets.Provider.PostgreSQL.Data;

/// <summary>Deterministic IDs for the global domain asset type seed catalog (see <see cref="DomainLayerIds"/>).</summary>
internal static class DomainAssetTypeIds
{
    public static readonly Guid Pole                = Guid.Parse("00000000-0000-0000-0000-000000000004");
    public static readonly Guid Transformer         = Guid.Parse("00000000-0000-0000-0000-000000000005");
    public static readonly Guid LowTensionWire      = Guid.Parse("00000000-0000-0000-0000-000000000006");
    public static readonly Guid WaterDischargePoint = Guid.Parse("00000000-0000-0000-0000-000000000007");
    public static readonly Guid Breaker             = Guid.Parse("00000000-0000-0000-0000-000000000008");
    public static readonly Guid IpTransformer       = Guid.Parse("00000000-0000-0000-0000-000000000009");
    public static readonly Guid IpPole              = Guid.Parse("00000000-0000-0000-0000-00000000000a");
    public static readonly Guid IpLampGroup24h      = Guid.Parse("00000000-0000-0000-0000-00000000000b");
}
