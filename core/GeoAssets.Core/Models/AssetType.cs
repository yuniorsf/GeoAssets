namespace GeoAssets.Core.Models;

/// <summary>User-defined asset category (e.g. "Water Tower", "Road", "Survey Area")</summary>
public sealed class AssetType
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#3388ff";
    public string IconUrl { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; } = false;

    // Built-in default types
    public static readonly AssetType Point = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
        Name = "Punto de interés",
        Color = "#e74c3c",
        IsBuiltIn = true
    };

    public static readonly AssetType Line = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
        Name = "Línea",
        Color = "#3498db",
        IsBuiltIn = true
    };

    public static readonly AssetType Area = new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
        Name = "Área",
        Color = "#2ecc71",
        IsBuiltIn = true
    };

    public static IEnumerable<AssetType> Defaults => [Point, Line, Area];
}
