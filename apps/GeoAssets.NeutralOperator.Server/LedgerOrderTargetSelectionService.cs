using System.Globalization;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// This host's <see cref="IOrderTargetSelectionService"/> (XD01-382, ADR-001 §3.4 D2): the selection of a NeutralOperator
/// order's targets is the kernel's, through its global target ledger, so exclusivity holds across verticals — while the
/// order, its rules and its local target views stay here.
/// <list type="number">
///   <item><b>Authorize locally</b>, exactly as the engine's in-process selection does: the order must exist
///   (<see cref="KeyNotFoundException"/>), <c>ServiceOrderRules</c> must grant <see cref="OrderAction.View"/> (preview) or the
///   target write (apply, <see cref="OrderTargetWrite.AddOrRemove"/>) — a caller outside the order's organization is refused
///   with the unknown-order answer —, its type must be registered and declare a target policy, and an apply needs an open order.</item>
///   <item><b>Preview</b> → <c>POST /api/targets/ledger/neutral-operator/{orderId}/reserve</c> with <c>preview: true</c>;
///   nothing is written anywhere.</item>
///   <item><b>Apply</b> → the same route without preview: the kernel resolves the strategy over its spatial plane, restricted
///   to the order type's policy and the order's organization (named in the body — never a header), checks exclusivity
///   against every vertical and reserves. A reservation another order's exclusive hold blocks throws
///   <see cref="TargetLedgerConflictException"/> (409 <c>targets.conflict</c>); a kernel that cannot serve the call throws
///   <see cref="TargetLedgerUnavailableException"/> (503).</item>
///   <item><b>Mirror</b>: after a successful reservation, every open target the ledger holds for the order (read page by
///   page from <c>…/usages</c>) is added to this host's own <c>ServiceOrderTargets</c> — same target id, as an in-process
///   target of the local order — so the order-detail target routes, occupancy staging and the shared-use dispatch read
///   them locally. The add is idempotent on (order, feature): a retried apply re-reserves nothing and re-mirrors safely.</item>
///   <item><b>Record</b> the selection on the order and its <see cref="OrderAction.Annotate"/> action-log entry, as the engine does.</item>
/// </list>
/// Release is <see cref="LedgerTargetReleaseLifecycleHook"/>'s, when the order closes.
/// </summary>
internal sealed class LedgerOrderTargetSelectionService(
    IServiceOrderRepository orders,
    OrderTypeRegistry orderTypes,
    ServiceOrderRules rules,
    ITargetLedgerClient ledger,
    IServiceOrderTargetWriter targets,
    TimeProvider timeProvider) : IOrderTargetSelectionService
{
    /// <summary>Ledger entries read per usages page when mirroring (the ledger's maximum).</summary>
    public const int MirrorPageSize = 1000;

    public async Task<TargetSelectionPreview> PreviewAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);

        var order = await AuthorizeAsync(request, principal, OrderAction.View, ct).ConfigureAwait(false);
        return await ledger.PreviewAsync(Command(order, request, principal), ct).ConfigureAwait(false);
    }

    public async Task<TargetSelectionResult> ApplyAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(principal);

        var order = await AuthorizeAsync(request, principal, OrderAction.Annotate, ct).ConfigureAwait(false);
        var result = await ledger.ReserveAsync(Command(order, request, principal), ct).ConfigureAwait(false);
        await MirrorAsync(order, ct).ConfigureAwait(false);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (order is ServiceOrder serviceOrder)
        {
            serviceOrder.SelectionSpec = new FeatureSelectionSpec
            {
                StrategyId = request.StrategyId,
                Parameters = request.Parameters,
                ExecutedAt = now,
            };
            serviceOrder.UpdatedAt = now;
            await orders.UpdateAsync(serviceOrder, ct).ConfigureAwait(false);
        }

        await orders.AppendActionAsync(
            order.Id,
            new OrderActionLog(OrderAction.Annotate, principal.UserId, now, Summary(request.StrategyId, result)) { ActorKind = principal.Kind },
            ct).ConfigureAwait(false);

        return result;
    }

    /// <summary>The ledger key of a NeutralOperator order.</summary>
    public static TargetLedgerKey KeyOf(string orderId) => new(NeutralOperatorModule.ModuleId, orderId);

    private async Task<IServiceOrder> AuthorizeAsync(TargetSelectionRequest request, WorkflowPrincipal principal, OrderAction action, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OrderId, nameof(request));
        ArgumentException.ThrowIfNullOrWhiteSpace(request.StrategyId, nameof(request));

        var order = await orders.GetByIdAsync(request.OrderId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"ServiceOrder '{request.OrderId}' not found.");

        var verdict = action == OrderAction.Annotate
            ? rules.EvaluateTargetWrite(principal, order, OrderTargetWrite.AddOrRemove)
            : rules.Evaluate(principal, action, order);
        if (!verdict.Allowed)
            throw new OrderActionDeniedException(order.Id, verdict);

        var orderType = orderTypes.Find(order.OrderTypeId)
            ?? throw new KeyNotFoundException($"Order type '{order.OrderTypeId}' is not registered.");
        if (orderType.TargetPolicy is null)
            throw new InvalidOperationException($"Order type '{orderType.Id}' declares no target policy, so its orders hold no targets.");
        if (action == OrderAction.Annotate && ServiceOrderTransitions.IsTerminalState(orderType, order.Status))
            throw new InvalidServiceOrderTransitionException(order.Status, order.Status);

        return order;
    }

    private static TargetLedgerReserveCommand Command(IServiceOrder order, TargetSelectionRequest request, WorkflowPrincipal principal) =>
        new(
            KeyOf(order.Id),
            order.OrganizationId,
            order.OrderTypeId,
            request.StrategyId,
            request.Parameters,
            request.AcceptTruncation,
            request.Page,
            request.PageSize,
            principal.UserId);

    private async Task MirrorAsync(IServiceOrder order, CancellationToken ct)
    {
        var key = KeyOf(order.Id);
        var open = new List<ServiceOrderTarget>();
        for (var skip = 0; ; skip += MirrorPageSize)
        {
            var page = await ledger.GetUsagesAsync(key, order.OrganizationId, skip, MirrorPageSize, ct).ConfigureAwait(false);
            open.AddRange(page.Items
                .Where(entry => entry.ReleasedAt is null)
                .Select(entry => entry.Target with { OrderId = order.Id, VerticalId = TargetLedger.KernelVerticalId }));

            if (page.Items.Count == 0 || skip + page.Items.Count >= page.TotalCount) break;
        }

        if (open.Count > 0)
            await targets.AddRangeAsync(order.Id, open, ct).ConfigureAwait(false);
    }

    /// <summary>The action-log comment the engine writes: the strategy and the counts, never the selection's own values.</summary>
    private static string Summary(string strategyId, TargetSelectionResult result) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"Target selection '{strategyId}': matched {result.Matched}, added {result.Added}, already in order {result.AlreadyInOrder}, truncated {result.TruncatedByLimit}, warnings {result.WarningCount}, blocked {result.BlockedCount}.");
}
