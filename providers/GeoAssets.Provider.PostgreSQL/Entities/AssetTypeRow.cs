namespace GeoAssets.Provider.PostgreSQL.Entities;

/// <summary>EF Core entity that maps to the <c>asset_type</c> table.</summary>
public sealed class AssetTypeRow
{
    public Guid   Id        { get; set; } = Guid.NewGuid();
    public string Name      { get; set; } = string.Empty;
    public string Color     { get; set; } = "#3388ff";
    public string IconUrl   { get; set; } = string.Empty;
    public bool   IsBuiltIn { get; set; } = false;
}
