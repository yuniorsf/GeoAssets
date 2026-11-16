using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Selection.Strategies;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Selection;

/// <summary>
/// Runs a selection strategy on behalf of a request that cannot be trusted for scope: whatever
/// the caller's parameters say, the result holds only features of one organization and of the
/// allowed asset types, and the run is rejected — not truncated — once it matches more features
/// than the ceiling.
///
/// The restrictions are applied twice: pushed into the strategy through its parameters and the
/// selection context (so a store can apply them in its query), and again on the returned
/// features, because not every strategy reads those parameters.
/// </summary>
public sealed class BoundedFeatureSelector
{
    /// <summary>The parameter both bounded strategies read their asset-type filter from.</summary>
    public const string AssetTypeIdsParameter = PolygonFenceSelectionStrategy.AssetTypeIdsParameter;

    /// <summary>The parameter both bounded strategies read their result ceiling from.</summary>
    public const string MaxResultsParameter = PolygonFenceSelectionStrategy.MaxResultsParameter;

    private readonly FeatureSelectionRegistry _registry;
    private readonly IAssetSpatialQuery _spatialQuery;
    private readonly IAssetProvider? _assets;
    private readonly TimeProvider _timeProvider;

    /// <param name="registry">Where strategies are looked up.</param>
    /// <param name="spatialQuery">The store-backed query the spatial strategies run on.</param>
    /// <param name="assets">
    /// The asset provider handed to the older strategies that scan it directly. Optional: the
    /// bounded strategies (<c>polygon-fence</c>, <c>identifier-list</c>) never touch it.
    /// </param>
    /// <param name="timeProvider">Clock for the audit record of a run that never reaches a strategy.</param>
    public BoundedFeatureSelector(
        FeatureSelectionRegistry registry,
        IAssetSpatialQuery spatialQuery,
        IAssetProvider? assets = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(spatialQuery);

        _registry = registry;
        _spatialQuery = spatialQuery;
        _assets = assets;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>Whether a strategy with this id is registered (ids compare case-insensitively).</summary>
    public bool HasStrategy(string strategyId) =>
        strategyId is not null
        && _registry.GetAvailableStrategies().Any(m => m.StrategyId.Equals(strategyId, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Runs the strategy and returns its distinct matches ordered by id.
    /// <list type="bullet">
    /// <item><description>An unknown strategy throws <see cref="KeyNotFoundException"/>.</description></item>
    /// <item><description>
    /// The asset-type filter becomes the caller's list intersected with
    /// <see cref="BoundedFeatureSelectionRequest.AllowedAssetTypeIds"/>; when that intersection
    /// is empty nothing can match, and no strategy or store is called.
    /// </description></item>
    /// <item><description>
    /// The strategy is asked for at most one feature more than the ceiling (fewer when the
    /// caller asked for fewer); a run matching more than the ceiling throws
    /// <see cref="SelectionTooLargeException"/>.
    /// </description></item>
    /// <item><description>Parameter errors surface as the strategy threw them.</description></item>
    /// </list>
    /// </summary>
    public async Task<BoundedFeatureSelection> SelectAsync(BoundedFeatureSelectionRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StrategyId, nameof(request));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.Ceiling, nameof(request));

        if (!HasStrategy(request.StrategyId))
            throw new KeyNotFoundException($"Selection strategy '{request.StrategyId}' not found.");

        var parameters = new Dictionary<string, object>(request.Parameters, StringComparer.Ordinal);

        var assetTypeIds = EffectiveAssetTypeIds(request);
        if (assetTypeIds is not null)
            parameters[AssetTypeIdsParameter] = assetTypeIds;
        parameters[MaxResultsParameter] = EffectiveMaxResults(request);
        ApplyDefaultAttributeKey(request, parameters);

        if (assetTypeIds is { Count: 0 })
            return new BoundedFeatureSelection([], NothingRan(request.StrategyId, parameters));

        var context = new FeatureSelectionContext
        {
            Repository = _assets!,
            SpatialQuery = _spatialQuery,
            OrganizationId = request.OrganizationId,
            TargetOrder = request.TargetOrder,
            OrderRepository = request.OrderRepository,
            Parameters = parameters,
        };

        var (features, spec) = await _registry.SelectAsync(request.StrategyId, context, note: null, ct).ConfigureAwait(false);

        var allowedTypes = assetTypeIds?.ToHashSet(StringComparer.Ordinal);
        var matched = features
            .Where(f => f is not null && f.Properties.OrganizationId == request.OrganizationId)
            .Where(f => allowedTypes is null || allowedTypes.Contains(f.Properties.AssetTypeId))
            .DistinctBy(f => f.Id, StringComparer.Ordinal)
            .OrderBy(f => f.Id, StringComparer.Ordinal)
            .ToList();

        if (matched.Count > request.Ceiling)
            throw new SelectionTooLargeException(matched.Count, request.Ceiling);

        return new BoundedFeatureSelection(matched, spec);
    }

    /// <summary>
    /// Null when no type filter applies at all; otherwise the list the strategy must filter by,
    /// which is empty when the caller asked only for types that are not allowed.
    /// </summary>
    private static List<string>? EffectiveAssetTypeIds(BoundedFeatureSelectionRequest request)
    {
        var requested = request.Parameters.GetOptionalStringList(AssetTypeIdsParameter);
        var asked = requested is { Count: > 0 } ? requested.Where(id => id is not null).Distinct(StringComparer.Ordinal).ToList() : null;

        if (request.AllowedAssetTypeIds is not { } allowed)
            return asked;

        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        return asked is null ? [.. allowedSet] : [.. asked.Where(allowedSet.Contains)];
    }

    private static int EffectiveMaxResults(BoundedFeatureSelectionRequest request)
    {
        int? requested;
        try
        {
            requested = request.Parameters.GetOptionalInt32(MaxResultsParameter);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
        {
            throw new InvalidCastException($"Parameter '{MaxResultsParameter}' is not an integer.", ex);
        }

        if (requested is { } asked)
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(asked, nameof(request));

        var onePastTheCeiling = request.Ceiling == int.MaxValue ? int.MaxValue : request.Ceiling + 1;
        return Math.Min(requested ?? int.MaxValue, onePastTheCeiling);
    }

    private static void ApplyDefaultAttributeKey(BoundedFeatureSelectionRequest request, Dictionary<string, object> parameters)
    {
        if (!IsIdentifierList(request.StrategyId)) return;
        if (IsSupplied(parameters, IdentifierListSelectionStrategy.AttributeKeyParameter)) return;

        parameters[IdentifierListSelectionStrategy.AttributeKeyParameter] = request.DefaultAttributeKey
            ?? throw new ArgumentException(
                $"Parameter '{IdentifierListSelectionStrategy.AttributeKeyParameter}' is required: no default identifier attribute applies.",
                nameof(request));
    }

    /// <summary>Whether <paramref name="strategyId"/> names the <c>identifier-list</c> strategy.</summary>
    public static bool IsIdentifierList(string strategyId) =>
        string.Equals(strategyId, IdentifierListSelectionStrategy.Id, StringComparison.OrdinalIgnoreCase);

    private static bool IsSupplied(Dictionary<string, object> parameters, string key) =>
        parameters.TryGetValue(key, out var value)
        && value is not null and not System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.Null };

    private FeatureSelectionSpec NothingRan(string strategyId, Dictionary<string, object> parameters) => new()
    {
        StrategyId = strategyId,
        Parameters = parameters,
        ExecutedAt = _timeProvider.GetUtcNow().UtcDateTime,
    };
}
