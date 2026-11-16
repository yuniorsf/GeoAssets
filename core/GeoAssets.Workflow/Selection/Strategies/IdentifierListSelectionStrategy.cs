using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;

namespace GeoAssets.Workflow.Selection.Strategies;

/// <summary>
/// Selects features from a pasted or uploaded list of identifiers, matched against one attribute
/// (e.g. a pole's plate number). Values are trimmed, blank entries are dropped, and duplicates
/// are removed with ordinal comparison before the lookup.
///
/// Required parameters:
///   attributeKey  (string)                    — the custom attribute to match; the reserved key
///                                               <c>"id"</c> matches the feature id instead
///   values        (IEnumerable&lt;string&gt;) — the identifiers to look up; at least one, and at most
///                                               <see cref="MaxDistinctValues"/> after normalization
///
/// Optional parameters:
///   assetTypeIds  (IEnumerable&lt;string&gt;) — keep only these asset types; absent or empty keeps all
///   maxResults    (int, default unbounded)  — cut the result, ordered by feature id, at this size
///
/// One value matching several features returns all of them, and a value matching none is simply
/// absent from the result — reporting either is the caller's job. Results are ordered by feature
/// id (ordinal, ascending).
/// </summary>
[ExportFeatureSelectionStrategy(Id,
    Category    = "Filter",
    DisplayName = "Identifier List",
    Description = "Selects features whose attribute matches any identifier in a list.")]
public sealed class IdentifierListSelectionStrategy : IFeatureSelectionStrategy
{
    public const string Id                    = "identifier-list";
    public const string AttributeKeyParameter = "attributeKey";
    public const string ValuesParameter       = "values";
    public const string AssetTypeIdsParameter = "assetTypeIds";
    public const string MaxResultsParameter   = "maxResults";

    /// <summary>The reserved <c>attributeKey</c> that matches <see cref="GeoFeature.Id"/>.</summary>
    public const string FeatureIdKey = AssetSpatialQueryKeys.FeatureId;

    /// <summary>Ceiling on the number of distinct identifiers in one selection.</summary>
    public const int MaxDistinctValues = 5000;

    public string StrategyId  => Id;
    public string DisplayName => "Identifier List";
    public string Description => "Selects features whose attribute matches any identifier in a list.";

    public Task<IReadOnlyList<GeoFeature>> SelectAsync(
        IFeatureSelectionContext context,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        var p = context.Parameters;

        var attributeKey = p.GetString(AttributeKeyParameter);
        if (string.IsNullOrWhiteSpace(attributeKey))
            throw new ArgumentException($"Parameter '{AttributeKeyParameter}' must not be blank.", nameof(context));

        var values = Normalize(p.GetStringList(ValuesParameter));
        if (values.Count == 0)
            throw new ArgumentException($"Parameter '{ValuesParameter}' holds no non-blank identifier.", nameof(context));

        if (values.Count > MaxDistinctValues)
            throw new ArgumentOutOfRangeException(
                nameof(context),
                values.Count,
                $"Parameter '{ValuesParameter}' holds more than {MaxDistinctValues} distinct identifiers.");

        var assetTypeIds = p.GetOptionalStringList(AssetTypeIdsParameter);
        var maxResults   = p.GetOptionalInt32(MaxResultsParameter) ?? int.MaxValue;
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResults);

        var filter = new AssetSpatialFilter(
            assetTypeIds is { Count: > 0 } ? assetTypeIds : null,
            context.OrganizationId,
            maxResults);

        return context.SpatialQuery.FindByAttributeValuesAsync(attributeKey, values, filter, ct);
    }

    private static List<string> Normalize(IReadOnlyList<string> raw)
    {
        var seen   = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<string>();

        foreach (var entry in raw)
        {
            var value = entry?.Trim();
            if (!string.IsNullOrEmpty(value) && seen.Add(value))
                result.Add(value);
        }

        return result;
    }
}
