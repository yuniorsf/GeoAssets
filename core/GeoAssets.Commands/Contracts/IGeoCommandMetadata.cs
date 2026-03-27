namespace GeoAssets.Commands.Contracts;

/// <summary>
/// MEF metadata contract — read from [ExportGeoCommand] without instantiating the handler.
/// </summary>
public interface IGeoCommandMetadata
{
    string Name        { get; }
    string Category    { get; }
    string Description { get; }
}
