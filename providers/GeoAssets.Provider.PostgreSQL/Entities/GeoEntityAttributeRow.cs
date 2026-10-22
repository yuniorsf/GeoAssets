namespace GeoAssets.Provider.PostgreSQL.Entities;

public sealed class GeoEntityAttributeRow
{
    public string EntityId { get; set; } = string.Empty;
    public string Key      { get; set; } = string.Empty;
    public string Value    { get; set; } = string.Empty;

    public GeoEntityRow? Entity { get; set; }
}
