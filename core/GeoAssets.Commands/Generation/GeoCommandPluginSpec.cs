namespace GeoAssets.Commands.Generation;

public sealed record class GeoCommandPluginSpec
{
    public string PluginName { get; init; } = string.Empty;
    public string CommandName { get; init; } = string.Empty;
    public string Category { get; init; } = "General";
    public string Description { get; init; } = string.Empty;
    public IReadOnlyList<string> Usings { get; init; } = [];
    public IReadOnlyList<GeoCommandPluginParameterSpec> Parameters { get; init; } = [];
    public string HandlerBody { get; init; } = string.Empty;
}
