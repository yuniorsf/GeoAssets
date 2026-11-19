using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Selection.Strategies;

namespace GeoAssets.Workflow.Targets;

/// <summary>
/// Default <see cref="IOrderTargetSelectionService"/>, written against ports only: the order
/// store, the target store, the selection registry (through <see cref="BoundedFeatureSelector"/>)
/// and the order rules. The contract is the documentation on the interface; what follows is how.
///
/// <para><b>Authorization comes first.</b> The target store does not check who is asking — its
/// per-order members deliberately ignore the tenant filter — so no target is read or written
/// until <see cref="ServiceOrderRules"/> has allowed the principal on the order.</para>
///
/// <para><b>Apply is ordered for retries.</b> Targets are added first
/// (<see cref="IServiceOrderTargetWriter.AddRangeAsync"/> skips features the order already
/// holds), then the selection is recorded on the order, then the action-log entry is appended.
/// A call that fails part-way can simply be repeated: it adds nothing twice and completes the
/// steps that were missing.</para>
/// </summary>
public sealed class OrderTargetSelectionService : IOrderTargetSelectionService
{
    /// <summary>Longest alias the target store accepts; a longer attribute value is cut to it.</summary>
    public const int MaxAliasLength = 256;

    private readonly IServiceOrderRepository _orders;
    private readonly OrderTypeRegistry _orderTypes;
    private readonly ServiceOrderRules _rules;
    private readonly BoundedFeatureSelector _selector;
    private readonly IServiceOrderTargetReader _targetReader;
    private readonly IServiceOrderTargetWriter _targetWriter;
    private readonly OrderTargetSelectionOptions _options;
    private readonly TimeProvider _timeProvider;

    public OrderTargetSelectionService(
        IServiceOrderRepository orders,
        OrderTypeRegistry orderTypes,
        ServiceOrderRules rules,
        BoundedFeatureSelector selector,
        IServiceOrderTargetReader targetReader,
        IServiceOrderTargetWriter targetWriter,
        OrderTargetSelectionOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(orderTypes);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(targetReader);
        ArgumentNullException.ThrowIfNull(targetWriter);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _orders = orders;
        _orderTypes = orderTypes;
        _rules = rules;
        _selector = selector;
        _targetReader = targetReader;
        _targetWriter = targetWriter;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<TargetSelectionPreview> PreviewAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);

        var plan = await PlanAsync(request, principal, OrderAction.View, ct).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        var skip = (long)(request.Page - 1) * request.PageSize;
        var page = skip >= plan.Kept.Count
            ? []
            : plan.Kept.Skip((int)skip).Take(request.PageSize).Select(f => ToTarget(plan, f, principal, now)).ToList();

        return new TargetSelectionPreview
        {
            OrderTypeId = plan.Order.OrderTypeId,
            Matched = plan.Matched,
            Eligible = plan.Eligible,
            AlreadyInOrder = plan.Matched - plan.Eligible,
            TruncatedByLimit = plan.Eligible - plan.Kept.Count,
            WarningCount = plan.CountAt(OrderTargetConflictLevel.Warning),
            BlockedCount = plan.CountAt(OrderTargetConflictLevel.Blocked),
            FirstPage = new PagedResult<ServiceOrderTarget> { Items = page, TotalCount = plan.Kept.Count },
            NotFoundValues = plan.NotFoundValues,
            AmbiguousValues = plan.AmbiguousValues,
        };
    }

    public async Task<TargetSelectionResult> ApplyAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);

        var plan = await PlanAsync(request, principal, OrderAction.Annotate, ct).ConfigureAwait(false);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var targets = plan.Kept.Select(f => ToTarget(plan, f, principal, now)).ToList();

        // Last exit before the first write: a caller that gave up leaves the order untouched.
        ct.ThrowIfCancellationRequested();

        var added = await _targetWriter.AddRangeAsync(plan.Order.Id, targets, ct).ConfigureAwait(false);

        var result = new TargetSelectionResult
        {
            OrderTypeId = plan.Order.OrderTypeId,
            Added = added,
            Matched = plan.Matched,
            Eligible = plan.Eligible,
            AlreadyInOrder = plan.Matched - plan.Eligible,
            TruncatedByLimit = plan.Eligible - plan.Kept.Count,
            WarningCount = plan.CountAt(OrderTargetConflictLevel.Warning),
            BlockedCount = plan.CountAt(OrderTargetConflictLevel.Blocked),
            NotFoundValues = plan.NotFoundValues,
            AmbiguousValues = plan.AmbiguousValues,
        };

        // Only ServiceOrder can carry a new SelectionSpec; another IServiceOrder implementation
        // still gets its targets and its action-log entry.
        if (plan.Order is ServiceOrder order)
        {
            order.SelectionSpec = plan.Spec;
            order.UpdatedAt = now;
            await _orders.UpdateAsync(order, ct).ConfigureAwait(false);
        }

        await _orders.AppendActionAsync(
            plan.Order.Id,
            new OrderActionLog(OrderAction.Annotate, principal.UserId, now, Summary(plan.Spec.StrategyId, result))
            {
                ActorKind = principal.Kind,
            },
            ct).ConfigureAwait(false);

        return result;
    }

    // ── Shared by preview and apply ───────────────────────────────────────────

    private async Task<SelectionPlan> PlanAsync(
        TargetSelectionRequest request, WorkflowPrincipal principal, OrderAction action, CancellationToken ct)
    {
        var (order, policy) = await AuthorizeAsync(request, principal, action, ct).ConfigureAwait(false);

        var selection = await _selector.SelectAsync(
            new BoundedFeatureSelectionRequest(request.StrategyId, request.Parameters, order.OrganizationId, _options.MaxMatched)
            {
                AllowedAssetTypeIds = policy.TargetAssetTypeIds,
                DefaultAttributeKey = policy.AliasAttributeKey,
                TargetOrder = order,
                OrderRepository = _orders,
            },
            ct).ConfigureAwait(false);

        var held = await HeldFeatureIdsAsync(order.Id, ct).ConfigureAwait(false);
        var eligible = selection.Features.Where(f => !held.Contains(f.Id)).ToList();

        var kept = eligible;
        if (policy.SelectionLimit is { } limit)
        {
            var remaining = Math.Max(0, limit - held.Count);
            if (eligible.Count > remaining)
            {
                if (action == OrderAction.Annotate && !request.AcceptTruncation)
                    throw new SelectionLimitExceededException(limit, held.Count, eligible.Count);

                kept = eligible.GetRange(0, remaining);
            }
        }

        var keptIds = kept.Select(f => f.Id).ToList();
        var usages = keptIds.Count == 0
            ? []
            : await _targetReader.FindOpenUsagesAsync(keptIds, order.Id, ct).ConfigureAwait(false);

        var (notFound, ambiguous) = IdentifierDiagnostics(selection);

        return new SelectionPlan(
            order,
            policy,
            selection.Spec,
            selection.Features.Count,
            eligible.Count,
            kept,
            TargetConflictEvaluator.Evaluate(order, policy.Exclusivity, keptIds, usages),
            notFound,
            ambiguous);
    }

    /// <summary>
    /// Every guard that precedes the selection itself, in the order the interface documents.
    /// Nothing of the target store is touched here.
    /// </summary>
    private async Task<(IServiceOrder Order, OrderTargetPolicy Policy)> AuthorizeAsync(
        TargetSelectionRequest request, WorkflowPrincipal principal, OrderAction action, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OrderId, nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StrategyId, nameof(request));

        var order = await _orders.GetByIdAsync(request.OrderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"ServiceOrder '{request.OrderId}' not found.");

        // XD01-316/333: applying a selection adds targets, which a recipient of the order's dispatch never may.
        var verdict = action == OrderAction.Annotate
            ? _rules.EvaluateTargetWrite(principal, order, OrderTargetWrite.AddOrRemove)
            : _rules.Evaluate(principal, action, order);
        if (!verdict.Allowed)
            throw new OrderActionDeniedException(order.Id, verdict);

        var orderType = _orderTypes.Find(order.OrderTypeId)
            ?? throw new KeyNotFoundException($"Order type '{order.OrderTypeId}' is not registered.");

        var policy = orderType.TargetPolicy
            ?? throw new InvalidOperationException($"Order type '{orderType.Id}' declares no target policy, so its orders hold no targets.");

        if (!_selector.HasStrategy(request.StrategyId))
            throw new KeyNotFoundException($"Selection strategy '{request.StrategyId}' not found.");

        if (action == OrderAction.Annotate && ServiceOrderTransitions.IsTerminalState(orderType, order.Status))
            throw new InvalidServiceOrderTransitionException(order.Status, order.Status);

        return (order, policy);
    }

    /// <summary>
    /// The feature ids the order already holds, read page by page through the reader port — the
    /// one way to ask that behaves identically on every adapter.
    /// </summary>
    private async Task<HashSet<string>> HeldFeatureIdsAsync(string orderId, CancellationToken ct)
    {
        var held = new HashSet<string>(StringComparer.Ordinal);

        for (var page = 1; ; page++)
        {
            var result = await _targetReader
                .GetPageAsync(new OrderTargetQuery(orderId, page, OrderTargetQuery.MaxPageSize), ct)
                .ConfigureAwait(false);

            foreach (var target in result.Items)
                held.Add(target.FeatureId);

            if (result.Items.Count < OrderTargetQuery.MaxPageSize || held.Count >= result.TotalCount)
                return held;
        }
    }

    private static ServiceOrderTarget ToTarget(SelectionPlan plan, GeoFeature feature, WorkflowPrincipal principal, DateTime addedAt)
    {
        var (longitude, latitude) = Position(feature);

        return new ServiceOrderTarget
        {
            Id = TargetId(plan.Order.Id, feature.Id),
            OrderId = plan.Order.Id,
            FeatureId = feature.Id,
            AssetTypeId = feature.Properties.AssetTypeId,
            Alias = Alias(plan.Policy, feature),
            Status = OrderTargetStatus.Pending,
            ConflictLevel = plan.ConflictLevels.GetValueOrDefault(feature.Id),
            Longitude = longitude,
            Latitude = latitude,
            AddedAt = addedAt,
            AddedBy = principal.UserId,
            OrganizationId = plan.Order.OrganizationId,
        };
    }

    /// <summary>
    /// The id of the target that holds <paramref name="featureId"/> in <paramref name="orderId"/>:
    /// a GUID derived from the pair, so a preview and the apply that follows — and a repeated
    /// apply — name the same target, and the id always fits the store's 36 characters.
    /// </summary>
    public static string TargetId(string orderId, string featureId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureId);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{orderId}|{featureId}"));
        return new Guid(hash.AsSpan(0, 16)).ToString();
    }

    private static string? Alias(OrderTargetPolicy policy, GeoFeature feature)
    {
        if (policy.AliasAttributeKey is not { } key) return null;

        var value = string.Equals(key, AssetSpatialQueryKeys.FeatureId, StringComparison.Ordinal)
            ? feature.Id
            : feature.Properties.CustomAttributes.GetValueOrDefault(key);

        return value is { Length: > MaxAliasLength } ? value[..MaxAliasLength] : value;
    }

    /// <summary>The feature's centroid as (longitude, latitude); nulls when it has no usable geometry.</summary>
    private static (double? Longitude, double? Latitude) Position(GeoFeature feature)
    {
        if (feature.Geometry is not { } geometry) return (null, null);

        var centroid = geometry.Centroid.Coordinates;
        return centroid is [var longitude, var latitude, ..] && double.IsFinite(longitude) && double.IsFinite(latitude)
            ? (longitude, latitude)
            : (null, null);
    }

    /// <summary>
    /// For an <c>identifier-list</c> selection: which requested identifiers matched nothing and
    /// which matched more than one feature. Empty for every other strategy.
    /// </summary>
    private static (IReadOnlyList<string> NotFound, IReadOnlyList<string> Ambiguous) IdentifierDiagnostics(BoundedFeatureSelection selection)
    {
        if (!BoundedFeatureSelector.IsIdentifierList(selection.Spec.StrategyId)) return ([], []);

        var parameters = selection.Spec.Parameters;
        var key = parameters.GetString(IdentifierListSelectionStrategy.AttributeKeyParameter);
        var requested = parameters.GetStringList(IdentifierListSelectionStrategy.ValuesParameter)
            .Select(v => v?.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .Select(v => v!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var matchesPerValue = selection.Features
            .Select(f => string.Equals(key, IdentifierListSelectionStrategy.FeatureIdKey, StringComparison.Ordinal)
                ? f.Id
                : f.Properties.CustomAttributes.GetValueOrDefault(key))
            .Where(v => v is not null)
            .GroupBy(v => v!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        return (
            [.. requested.Where(v => !matchesPerValue.ContainsKey(v))],
            [.. requested.Where(v => matchesPerValue.GetValueOrDefault(v) > 1)]);
    }

    /// <summary>The action-log comment: the strategy and the counts, never the selection's own values.</summary>
    private static string Summary(string strategyId, TargetSelectionResult result) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Target selection '{strategyId}': matched {result.Matched}, added {result.Added}, already in order {result.AlreadyInOrder}, truncated {result.TruncatedByLimit}, warnings {result.WarningCount}, blocked {result.BlockedCount}.");

    private sealed record SelectionPlan(
        IServiceOrder Order,
        OrderTargetPolicy Policy,
        FeatureSelectionSpec Spec,
        int Matched,
        int Eligible,
        List<GeoFeature> Kept,
        IReadOnlyDictionary<string, OrderTargetConflictLevel> ConflictLevels,
        IReadOnlyList<string> NotFoundValues,
        IReadOnlyList<string> AmbiguousValues)
    {
        public int CountAt(OrderTargetConflictLevel level) => ConflictLevels.Values.Count(l => l == level);
    }
}
