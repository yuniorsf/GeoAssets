using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;

namespace GeoAssets.Plugin.GeoJsonImport;

/// <summary>
/// Reads a GeoJSON file and loads its features and custom asset types into an <see cref="IAssetProvider"/> —
/// the active provider of the host that runs the command, whatever backs it (XD01-389: the plugin no longer
/// opens a database of its own; only <c>GeoAssets.Server</c> connects to the core asset database).
/// </summary>
public static class GeoJsonFileImporter
{
    /// <summary>
    /// Reads <paramref name="geoJsonPath"/> and adds its features to <paramref name="target"/>, after the file's
    /// custom (non-built-in) asset types. A feature id that appears twice in the file is imported once, with
    /// its last occurrence; an asset type id that appears twice is added once, with its first.
    /// </summary>
    /// <param name="geoJsonPath">Absolute or relative path to the .geojson file.</param>
    /// <param name="target">The provider the features are written to.</param>
    /// <param name="ct">Cancels reading the file.</param>
    /// <returns>The number of features imported.</returns>
    public static async Task<int> ImportAsync(string geoJsonPath, IAssetProvider target, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(geoJsonPath);
        ArgumentNullException.ThrowIfNull(target);

        var json = await File.ReadAllTextAsync(geoJsonPath, ct).ConfigureAwait(false);

        var collection = GeoJsonSerializer.Deserialize(json)
            ?? throw new InvalidOperationException($"Failed to parse GeoJSON file: {geoJsonPath}");

        var assetTypes = new Dictionary<Guid, AssetType>();
        foreach (var assetType in collection.Metadata.AssetTypes.Where(t => !t.IsBuiltIn))
            assetTypes.TryAdd(assetType.Id, assetType);

        var features = new Dictionary<string, GeoFeature>(StringComparer.Ordinal);
        foreach (var feature in collection.Features)
            features[feature.Id] = feature;

        foreach (var assetType in assetTypes.Values)
            target.AddAssetType(assetType);

        target.AddRange(features.Values);
        return features.Count;
    }
}
