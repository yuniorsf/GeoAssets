using GeoAssets.Core.Services;
using GeoAssets.Provider.InMemory;
using GeoAssets.Provider.PostgreSQL;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoAssets.Providers.Utils;

/// <summary>
/// Imports a GeoJSON file into an <see cref="InMemoryAssetProvider"/> and then
/// exports all features and asset types to a PostgreSQL database on Azure.
///
/// Typical usage:
/// <code>
///   var importer = new GeoJsonToPostgresImporter();
///   int count = await importer.ImportAsync("assets.geojson", "Host=...;Database=...;");
/// </code>
/// </summary>
public sealed class GeoJsonToPostgresImporter
{
    private readonly ILoggerFactory _loggerFactory;

    public GeoJsonToPostgresImporter(ILoggerFactory? loggerFactory = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
    }

    /// <summary>
    /// Reads <paramref name="geoJsonPath"/>, loads all features into an in-memory
    /// staging provider, then bulk-upserts them into a PostgreSQL database.
    /// </summary>
    /// <param name="geoJsonPath">Absolute or relative path to the .geojson file.</param>
    /// <param name="connectionString">
    ///   Npgsql connection string, e.g.
    ///   <c>Host=myserver.postgres.database.azure.com;Database=geoassets;Username=...;Password=...;Ssl Mode=Require;</c>
    /// </param>
    /// <returns>Number of features imported.</returns>
    public async Task<int> ImportAsync(
        string geoJsonPath,
        string connectionString,
        CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(geoJsonPath, ct);

        var collection = GeoJsonSerializer.Deserialize(json)
            ?? throw new InvalidOperationException($"Failed to parse GeoJSON file: {geoJsonPath}");

        // Stage features in-memory
        var staging = new InMemoryAssetProvider();
        staging.LoadAll(collection.Features);
        foreach (var assetType in collection.Metadata.AssetTypes)
            staging.AddAssetType(assetType);

        // Connect to PostgreSQL and transfer
        var factory = new PostgresProviderFactory(_loggerFactory);
        var postgres = factory.Create(connectionString);
        try
        {
            foreach (var assetType in staging.GetAssetTypes())
                postgres.AddAssetType(assetType);

            var features = staging.GetAll();
            postgres.AddRange(features);
            return features.Count;
        }
        finally
        {
            if (postgres is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
        }
    }
}
