namespace GeoAssets.Workflow.Targets;

/// <summary>
/// The sort keys <see cref="OrderTargetQuery.SortBy"/> accepts. Keys are case-sensitive, as
/// <see cref="GeoAssets.Core.Models.AssetQuery.SortBy"/>'s are.
/// </summary>
public static class OrderTargetSortKeys
{
    public const string Alias = "alias";
    public const string Status = "status";
    public const string ConflictLevel = "conflictLevel";
    public const string AddedAt = "addedAt";

    /// <summary>The default key, used whenever no other key is recognized.</summary>
    public const string FeatureId = "featureId";

    /// <summary>
    /// Maps a requested sort key to the key actually applied: <paramref name="sortBy"/> itself
    /// when it is <see cref="Alias"/>, <see cref="Status"/>, <see cref="ConflictLevel"/> or
    /// <see cref="AddedAt"/>; <see cref="FeatureId"/> for null or anything else. Never throws.
    /// </summary>
    public static string Resolve(string? sortBy) => sortBy switch
    {
        Alias => Alias,
        Status => Status,
        ConflictLevel => ConflictLevel,
        AddedAt => AddedAt,
        _ => FeatureId,
    };
}
