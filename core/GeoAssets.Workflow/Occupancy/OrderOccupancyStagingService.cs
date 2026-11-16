using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Occupancy;

/// <summary>
/// Default <see cref="IOrderOccupancyStagingService"/>, written against ports only: the order
/// store, the target store, the pole occupancy reader, the change-set store and the order
/// rules. The contract is the documentation on the interface; what follows is how.
///
/// <para><b>Authorization comes first.</b> The target store checks nobody, and the occupancy
/// and change-set stores are scoped to the caller's organization by their adapters — so before
/// any of them is touched the rules must have allowed the principal on the order
/// <i>and</i> the principal must belong to the order's organization, which is the pole owner
/// (a selection only ever adds the order's own organization's features as targets).</para>
///
/// <para><b>No action-log entry</b> is appended for staging or discarding: the change set
/// itself records who staged it and when, and a repeated <c>PUT</c> must not grow the log.</para>
/// </summary>
public sealed class OrderOccupancyStagingService : IOrderOccupancyStagingService
{
    internal const string OwnerOnlyReason =
        "Occupancy is read and staged only by members of the organization that owns the order's poles.";

    private readonly IServiceOrderReader _orders;
    private readonly OrderTypeRegistry _orderTypes;
    private readonly ServiceOrderRules _rules;
    private readonly IServiceOrderTargetReader _targets;
    private readonly IPoleOccupancyReader _occupancy;
    private readonly IOccupancyChangeSetReader _changeSets;
    private readonly IOccupancyChangeSetWriter _changeSetWriter;
    private readonly OrderOccupancyOptions _options;
    private readonly TimeProvider _timeProvider;

    public OrderOccupancyStagingService(
        IServiceOrderReader orders,
        OrderTypeRegistry orderTypes,
        ServiceOrderRules rules,
        IServiceOrderTargetReader targets,
        IPoleOccupancyReader occupancy,
        IOccupancyChangeSetReader changeSets,
        IOccupancyChangeSetWriter changeSetWriter,
        OrderOccupancyOptions options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(orderTypes);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(occupancy);
        ArgumentNullException.ThrowIfNull(changeSets);
        ArgumentNullException.ThrowIfNull(changeSetWriter);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _orders = orders;
        _orderTypes = orderTypes;
        _rules = rules;
        _targets = targets;
        _occupancy = occupancy;
        _changeSets = changeSets;
        _changeSetWriter = changeSetWriter;
        _options = options;
        _timeProvider = timeProvider;
    }

    public async Task<TargetOccupancyView> GetAsync(string orderId, string targetId, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        await AuthorizeAsync(orderId, principal, OrderActionType.View, ct).ConfigureAwait(false);
        var target = await PoleTargetAsync(orderId, targetId, ct).ConfigureAwait(false);

        return await ViewAsync(target, ct).ConfigureAwait(false);
    }

    public async Task<PagedResult<OccupancyChangeSetView>> GetPageAsync(
        string orderId, int page, int pageSize, OccupancyChangeSetStatus? status, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        var query = new OccupancyChangeSetQuery(orderId, page, pageSize, status);
        await AuthorizeAsync(orderId, principal, OrderActionType.View, ct).ConfigureAwait(false);

        var result = await _changeSets.GetPageAsync(query, ct).ConfigureAwait(false);
        return new PagedResult<OccupancyChangeSetView>
        {
            Items = [.. result.Items.Select(OccupancyChangeSetView.From)],
            TotalCount = result.TotalCount,
        };
    }

    public async Task<TargetOccupancyView> StageAsync(
        string orderId, string targetId, IReadOnlyCollection<OccupancyChange> changes, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        ArgumentNullException.ThrowIfNull(changes);
        var order = await AuthorizeAsync(orderId, principal, OrderActionType.Annotate, ct).ConfigureAwait(false);

        if (ServiceOrderTransitions.IsTerminalState(_orderTypes.Find(order.OrderTypeId), order.Status))
            throw new InvalidServiceOrderTransitionException(order.Status, order.Status);

        var target = await PoleTargetAsync(orderId, targetId, ct).ConfigureAwait(false);
        var live = await _occupancy.GetForPoleAsync(target.FeatureId, ct).ConfigureAwait(false);
        var changeSet = Check(order, target, changes, live, principal);

        // Last exit before the write: a caller that gave up leaves what was staged as it was.
        ct.ThrowIfCancellationRequested();
        await _changeSetWriter.StageAsync(changeSet, ct).ConfigureAwait(false);

        return await ViewAsync(target, ct).ConfigureAwait(false);
    }

    public async Task<bool> DiscardAsync(string orderId, string targetId, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        await AuthorizeAsync(orderId, principal, OrderActionType.Annotate, ct).ConfigureAwait(false);
        await PoleTargetAsync(orderId, targetId, ct).ConfigureAwait(false);

        return await _changeSetWriter.DiscardAsync(orderId, targetId, ct).ConfigureAwait(false);
    }

    public async Task<int> DiscardAllAsync(string orderId, WorkflowPrincipal principal, CancellationToken ct)
    {
        await AuthorizeAsync(orderId, principal, OrderActionType.Annotate, ct).ConfigureAwait(false);

        return await _changeSetWriter.DiscardAllAsync(orderId, ct).ConfigureAwait(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>The order, once the rules allowed <paramref name="action"/> and the principal is one of the pole owner's own.</summary>
    private async Task<IServiceOrder> AuthorizeAsync(string orderId, WorkflowPrincipal principal, OrderActionType action, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentNullException.ThrowIfNull(principal);

        var order = await _orders.GetByIdAsync(orderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Service order '{orderId}' not found.");

        var verdict = _rules.Evaluate(principal, action, order);
        if (!verdict.Allowed)
            throw new OrderActionDeniedException(orderId, verdict);

        if (!Guid.TryParse(principal.OrganizationId, out var organizationId) || organizationId != order.OrganizationId)
            throw new OrderActionDeniedException(orderId, RuleEvaluationResult.Deny(action, OwnerOnlyReason));

        return order;
    }

    private async Task<ServiceOrderTarget> PoleTargetAsync(string orderId, string targetId, CancellationToken ct)
    {
        var target = await _targets.GetByIdAsync(orderId, targetId, ct).ConfigureAwait(false)
            ?? throw new OrderTargetNotFoundException(orderId, targetId);

        return _options.IsPole(target.AssetTypeId)
            ? target
            : throw new TargetIsNotAPoleException(targetId, target.AssetTypeId);
    }

    /// <summary>
    /// Builds the set and proves it applies to the pole as it is now, translating every reason
    /// it might not into the two exceptions the port documents.
    /// </summary>
    private OccupancyChangeSet Check(
        IServiceOrder order, ServiceOrderTarget target, IReadOnlyCollection<OccupancyChange> changes,
        PoleOccupancy? live, WorkflowPrincipal principal)
    {
        try
        {
            var changeSet = OccupancyChangeSet.Stage(
                order.Id, target.Id, target.FeatureId, order.OrganizationId, changes, live,
                principal.UserId, _timeProvider.GetUtcNow());
            OccupancyChangeApplier.Apply(live, changeSet);
            return changeSet;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            throw new InvalidOccupancyChangeException(ex.Message, ex);
        }
    }

    private async Task<TargetOccupancyView> ViewAsync(ServiceOrderTarget target, CancellationToken ct)
    {
        var live = await _occupancy.GetForPoleAsync(target.FeatureId, ct).ConfigureAwait(false);
        var staged = await _changeSets.GetStagedAsync(target.OrderId, target.Id, ct).ConfigureAwait(false);
        var current = OccupancyProjection.From(live);
        var currentVersion = live?.Version ?? PoleOccupancy.UnsavedVersion;

        var view = new TargetOccupancyView
        {
            OrderId = target.OrderId,
            TargetId = target.Id,
            PoleFeatureId = target.FeatureId,
            PoleOwnerOrganizationId = target.OrganizationId,
            CurrentVersion = currentVersion,
            Current = current,
            Preview = current,
        };
        if (staged is null) return view;

        var warnings = new List<string>();
        var isStale = staged.IsStaleAgainst(currentVersion);
        if (isStale) warnings.Add(OccupancyProblemCodes.StaleBaseline);

        var preview = Preview(live, staged, warnings);
        return view with
        {
            Staged = OccupancyChangeSetView.From(staged),
            BaselineVersion = staged.BaselineVersion,
            IsBaselineStale = isStale,
            Preview = preview,
            Warnings = warnings,
        };
    }

    /// <summary>The pole with the staged changes applied, or null — and a warning — when they no longer apply.</summary>
    private static OccupancyProjection? Preview(PoleOccupancy? live, OccupancyChangeSet staged, List<string> warnings)
    {
        try
        {
            return OccupancyChangeApplier.Apply(live, staged);
        }
        catch (OccupancyEntryNotFoundException)
        {
            warnings.Add(OccupancyProblemCodes.EntryNotFound);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            warnings.Add(OccupancyProblemCodes.InvalidChange);
        }

        return null;
    }
}
