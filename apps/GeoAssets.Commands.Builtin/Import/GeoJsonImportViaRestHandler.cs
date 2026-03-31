using GeoAssets.Commands.Contracts;
using GeoAssets.Core.Services;

namespace GeoAssets.Commands.Builtin.Import;

/// <summary>
/// Parses a GeoJSON string and bulk-loads all features into the active provider.
///
/// When the active provider is a <c>RestAssetProvider</c>, writes are forwarded
/// to the remote API (<c>POST /api/geoassets/features/bulk</c>), which persists
/// them to PostgreSQL — making this command the canonical path for browser-initiated
/// GeoJSON → REST API → PostgreSQL imports.
///
/// Parameters:
///   content  (string) — raw GeoJSON text (GeoFeatureCollection or GeoFeature[])
/// </summary>
[ExportGeoCommand("geojson-import-via-rest",
    Category    = "Import",
    Description = "Parses a GeoJSON string and loads all features into the active provider (REST → PostgreSQL when connected).")]
public sealed class GeoJsonImportViaRestHandler : IGeoCommandHandler
{
    public Task<GeoCommandResult> ExecuteAsync(
        GeoCommandContext context,
        IReadOnlyDictionary<string, object> parameters,
        CancellationToken ct = default)
    {
        if (!parameters.TryGetValue("content", out var raw) || raw is not string content)
            return Task.FromResult(GeoCommandResult.Fail("Required parameter 'content' is missing or not a string."));

        if (string.IsNullOrWhiteSpace(content))
            return Task.FromResult(GeoCommandResult.Fail("GeoJSON content is empty."));

        var collection = GeoJsonSerializer.Deserialize(content);
        if (collection is null)
            return Task.FromResult(GeoCommandResult.Fail("Failed to parse GeoJSON content."));

        foreach (var assetType in collection.Metadata.AssetTypes.Where(t => !t.IsBuiltIn))
            context.Repository.AddAssetType(assetType);

        context.Repository.AddRange(collection.Features);

        return Task.FromResult(GeoCommandResult.Ok(new
        {
            imported   = collection.Features.Count,
            assetTypes = collection.Metadata.AssetTypes.Count(t => !t.IsBuiltIn)
        }));
    }
}
