using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Provider.PostgreSQL;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoAssets.Providers.Utils;

/// <summary>
/// Imports a GeoJSON file into an in-memory staging buffer and then
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
    private readonly TimeProvider   _timeProvider;

    public GeoJsonToPostgresImporter(ILoggerFactory? loggerFactory = null, TimeProvider? timeProvider = null)
    {
        _loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
        _timeProvider  = timeProvider ?? TimeProvider.System;
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
        var staging = new StagingBuffer();
        staging.LoadAll(collection.Features);
        foreach (var assetType in collection.Metadata.AssetTypes)
            staging.AddAssetType(assetType);

        // Connect to PostgreSQL and transfer
        var factory = new PostgresProviderFactory(_loggerFactory, _timeProvider);
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

    /// <summary>
    /// Minimal in-memory staging buffer sized to exactly what <see cref="ImportAsync"/> needs
    /// (<see cref="LoadAll"/>, <see cref="AddAssetType"/>, <see cref="GetAssetTypes"/>,
    /// <see cref="GetAll"/>) — replaces the shared, general-purpose
    /// <c>InMemoryAssetProvider</c> (removed in XD01-131).
    /// </summary>
    private sealed class StagingBuffer
    {
        private readonly Dictionary<string, GeoFeature> _features = [];
        private readonly List<AssetType> _assetTypes = [.. AssetType.Defaults];

        public void LoadAll(IEnumerable<GeoFeature> features)
        {
            _features.Clear();
            foreach (var f in features) _features[f.Id] = f;
        }

        public void AddAssetType(AssetType assetType)
        {
            if (_assetTypes.All(t => t.Id != assetType.Id))
                _assetTypes.Add(assetType);
        }

        public IReadOnlyList<AssetType> GetAssetTypes() => [.. _assetTypes];

        public IReadOnlyList<GeoFeature> GetAll() => [.. _features.Values];
    }
}
