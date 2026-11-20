using GeoAssets.Commands.Contracts;

namespace GeoAssets.Plugin.GeoJsonImport;

/// <summary>
/// Command plugin that loads a GeoJSON file into the host's active provider (<see cref="GeoCommandContext.Repository"/>)
/// via <see cref="GeoJsonFileImporter"/>. Since XD01-389 it no longer connects to a database itself: where the
/// features land is the provider's business (a REST provider forwards them to <c>GeoAssets.Server</c>).
///
/// Drop the compiled DLL into the host's plugins/ directory; no recompilation needed.
///
/// Parameters:
///   filePath (string) — absolute or relative path to the .geojson file.
///
/// The former <c>connectionString</c> parameter is refused rather than ignored, so a caller that still names a
/// database is told the import goes to the active provider instead of being silently redirected.
/// </summary>
[ExportGeoCommand("geojson-import-file",
    Category    = "Import",
    Description = "Loads a GeoJSON file into the active provider.")]
public sealed class GeoJsonImportHandler : IGeoCommandHandler
{
    /// <summary>The parameter the command took before XD01-389; naming it now fails the command.</summary>
    public const string RetiredConnectionStringParameter = "connectionString";

    public async Task<GeoCommandResult> ExecuteAsync(
        GeoCommandContext context,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(parameters);

        if (parameters.ContainsKey(RetiredConnectionStringParameter))
        {
            return GeoCommandResult.Fail(
                $"Parameter '{RetiredConnectionStringParameter}' is no longer supported: the import writes to the active provider.");
        }

        if (!parameters.TryGetValue("filePath", out var fp) || fp is not string filePath || string.IsNullOrWhiteSpace(filePath))
            return GeoCommandResult.Fail("Required parameter 'filePath' is missing or not a string.");

        if (!File.Exists(filePath))
            return GeoCommandResult.Fail($"GeoJSON file not found: {filePath}");

        try
        {
            var count = await GeoJsonFileImporter.ImportAsync(filePath, context.Repository, ct).ConfigureAwait(false);
            return GeoCommandResult.Ok(new { imported = count, source = filePath });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return GeoCommandResult.Fail($"Import failed: {ex.Message}");
        }
    }
}
