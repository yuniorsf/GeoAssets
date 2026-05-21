namespace GeoAssets.Commands.Generation;

public sealed record class GeoCommandPluginParameterSpec
{
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = "object";
    public string Description { get; init; } = string.Empty;
    public bool Required { get; init; } = true;
}
