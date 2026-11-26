using GeoAssets.Core.FeatureImport;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;

namespace GeoAssets.Provider.PostgreSQL.Repositories;

/// <summary>
/// The checks <see cref="PostgresFeatureBulkImporter"/> runs on a chunk before anything reaches the database, in this
/// order: the id (<see cref="FeatureImportRejectionReasons.InvalidId"/>), the geometry's presence
/// (<see cref="FeatureImportRejectionReasons.MissingGeometry"/>), the asset type's visibility to the organization
/// (<see cref="FeatureImportRejectionReasons.UnknownAssetType"/>), its <see cref="AssetType.AllowedGeometryType"/>
/// (<see cref="FeatureImportRejectionReasons.GeometryMismatch"/>) and the name's length
/// (<see cref="FeatureImportRejectionReasons.NameTooLong"/>). An id sent more than once in the chunk is written once, from
/// its last occurrence.
/// </summary>
internal static class FeatureImportChunkValidator
{
    /// <summary>Longest accepted name (the <c>geo_entity.Name</c> column is <c>varchar(256)</c>).</summary>
    internal const int MaxNameLength = 256;

    /// <param name="features">The chunk's features; none may be <c>null</c>.</param>
    /// <param name="visibleAssetTypes">The asset types the organization may use, with their allowed geometry (<c>null</c> = any).</param>
    internal static ValidatedFeatureChunk Validate(
        IReadOnlyList<GeoFeature> features, IReadOnlyDictionary<Guid, GeometryType?> visibleAssetTypes)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(visibleAssetTypes);

        var lastIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < features.Count; i++)
        {
            var feature = features[i];
            if (feature?.Properties is null)
                throw new ArgumentException($"The chunk's feature #{i + 1} or its properties are null.", nameof(features));

            if (IsValidId(feature.Id))
                lastIndexById[feature.Id] = i;
        }

        var accepted = new List<GeoFeature>(lastIndexById.Count);
        var rejected = new List<FeatureImportRejection>();
        for (var i = 0; i < features.Count; i++)
        {
            var feature = features[i];
            if (FindRejection(feature, visibleAssetTypes) is { } reason)
                rejected.Add(new FeatureImportRejection(feature.Id ?? string.Empty, reason));
            else if (lastIndexById[feature.Id] == i)
                accepted.Add(feature);
        }

        return new ValidatedFeatureChunk(accepted, rejected);
    }

    private static string? FindRejection(GeoFeature feature, IReadOnlyDictionary<Guid, GeometryType?> visibleAssetTypes)
    {
        if (!IsValidId(feature.Id))
            return FeatureImportRejectionReasons.InvalidId;

        if (feature.Geometry is null)
            return FeatureImportRejectionReasons.MissingGeometry;

        if (!Guid.TryParse(feature.Properties.AssetTypeId, out var assetTypeId)
            || !visibleAssetTypes.TryGetValue(assetTypeId, out var allowedGeometry))
        {
            return FeatureImportRejectionReasons.UnknownAssetType;
        }

        if (allowedGeometry is { } allowed && feature.Geometry.GeometryType != allowed)
            return FeatureImportRejectionReasons.GeometryMismatch;

        if (feature.Properties.Name is { Length: > MaxNameLength })
            return FeatureImportRejectionReasons.NameTooLong;

        return null;
    }

    private static bool IsValidId(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= FeatureImportLimits.MaxFeatureIdLength;
}
