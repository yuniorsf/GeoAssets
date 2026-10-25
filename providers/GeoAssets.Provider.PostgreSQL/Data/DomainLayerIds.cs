namespace GeoAssets.Provider.PostgreSQL.Data;

/// <summary>Deterministic IDs for each domain asset type's default <see cref="Entities.LayerRow"/>.</summary>
internal static class DomainLayerIds
{
    public static readonly Guid Pole                = Guid.Parse("00000000-0000-0000-0001-000000000001");
    public static readonly Guid Transformer         = Guid.Parse("00000000-0000-0000-0001-000000000002");
    public static readonly Guid LowTensionWire      = Guid.Parse("00000000-0000-0000-0001-000000000003");
    public static readonly Guid WaterDischargePoint = Guid.Parse("00000000-0000-0000-0001-000000000004");
    public static readonly Guid Breaker             = Guid.Parse("00000000-0000-0000-0001-000000000005");
}
