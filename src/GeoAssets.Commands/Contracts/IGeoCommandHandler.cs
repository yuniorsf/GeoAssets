namespace GeoAssets.Commands.Contracts;

/// <summary>
/// Implemented by every GIS command handler — built-in or plugin.
/// Decorated with <see cref="ExportGeoCommandAttribute"/> for MEF discovery.
/// </summary>
public interface IGeoCommandHandler
{
    Task<GeoCommandResult> ExecuteAsync(
        GeoCommandContext context,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken ct = default);
}
