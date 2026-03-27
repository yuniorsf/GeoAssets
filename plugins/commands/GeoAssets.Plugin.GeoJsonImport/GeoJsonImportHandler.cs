using GeoAssets.Commands.Contracts;
using GeoAssets.Providers.Utils;

namespace GeoAssets.Plugin.GeoJsonImport;

/// <summary>
/// Command plugin that loads a GeoJSON file and saves all features to an
/// Azure Database for PostgreSQL instance via <see cref="GeoJsonToPostgresImporter"/>.
///
/// Drop the compiled DLL into the host's plugins/ directory; no recompilation needed.
///
/// Parameters:
///   filePath         (string) — absolute or relative path to the .geojson file.
///   connectionString (string) — Npgsql connection string for the Azure PostgreSQL database,
///                               e.g. "Host=myserver.postgres.database.azure.com;Database=geoassets;
///                                     Username=admin;Password=***;Ssl Mode=Require;"
/// </summary>
[ExportGeoCommand("geojson-import-postgres",
    Category    = "Import",
    Description = "Loads a GeoJSON file and saves all features to an Azure PostgreSQL database.")]
public sealed class GeoJsonImportHandler : IGeoCommandHandler
{
    public async Task<GeoCommandResult> ExecuteAsync(
        GeoCommandContext context,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken ct = default)
    {
        if (!parameters.TryGetValue("filePath", out var fp) || fp is not string filePath)
            return GeoCommandResult.Fail("Required parameter 'filePath' is missing or not a string.");

        if (!parameters.TryGetValue("connectionString", out var cs) || cs is not string connectionString)
            return GeoCommandResult.Fail("Required parameter 'connectionString' is missing or not a string.");

        if (!File.Exists(filePath))
            return GeoCommandResult.Fail($"GeoJSON file not found: {filePath}");

        try
        {
            var importer = new GeoJsonToPostgresImporter();
            var count = await importer.ImportAsync(filePath, connectionString, ct);
            return GeoCommandResult.Ok(new { imported = count, source = filePath });
        }
        catch (Exception ex)
        {
            return GeoCommandResult.Fail($"Import failed: {ex.Message}");
        }
    }
}
