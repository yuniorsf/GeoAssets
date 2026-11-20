using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.NeutralOperator.Server;

/// <summary>
/// The kernel's half of a NeutralOperator order's status change (XD01-382, ADR-001 §3.4), run in the transition's own
/// transaction (<see cref="IOrderLifecycleHook"/>):
/// <list type="bullet">
///   <item>When this host has a broker, stages <c>OrderStateChangedV1</c> on its outbox for every transition — with
///   <see cref="OrderStateChangedEvent.VerticalId"/> <c>neutral-operator</c>, <see cref="OrderStateChangedEvent.IsTerminal"/> and
///   <see cref="OrderStateChangedEvent.OrganizationId"/> — so the kernel's ledger consumer releases a closed order's targets
///   exactly once, whenever it receives it.</item>
///   <item>When the order closes, releases its targets in the kernel's ledger (<c>…/release</c>, idempotent; an order the
///   ledger holds nothing of is fine). Without a broker that call is the only release, so a kernel that cannot be reached
///   fails the transition (it rolls back; the caller retries) rather than leave the targets held for good. With a broker
///   the staged event guarantees the release, and a failed call is only logged.</item>
/// </list>
/// The local targets are released by the engine itself, in the same save.
/// </summary>
internal sealed partial class LedgerTargetReleaseLifecycleHook(
    ITargetLedgerClient ledger,
    IOutboxWriter outbox,
    NeutralOperatorMessaging messaging,
    ILogger<LedgerTargetReleaseLifecycleHook> logger) : IOrderLifecycleHook
{
    /// <summary>Who the kernel records as having released the targets, and the event's performer.</summary>
    public const string Actor = NeutralOperatorModule.ModuleId;

    public async Task OnTransitionedAsync(OrderTransitionContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var order = context.Order;
        if (messaging.HasBroker)
        {
            var changed = new OrderStateChangedEvent(
                order.Id, order.OrderTypeId, context.FromStatus, context.ToStatus, Actor, context.OccurredAt,
                VerticalId: NeutralOperatorModule.ModuleId, IsTerminal: context.IsTerminal, OrganizationId: order.OrganizationId);
            await outbox.EnqueueAsync(
                changed, IntegrationEventTypes.OrderStateChangedV1, partitionKey: $"{NeutralOperatorModule.ModuleId}/{order.Id}",
                ct: cancellationToken).ConfigureAwait(false);
        }

        // An order without an organization never reserved anything: the ledger requires one.
        if (!context.IsTerminal || order.OrganizationId == Guid.Empty) return;

        try
        {
            await ledger.ReleaseAsync(
                LedgerOrderTargetSelectionService.KeyOf(order.Id), order.OrganizationId, targetIds: null, Actor, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TargetLedgerUnavailableException ex) when (messaging.HasBroker)
        {
            LogReleaseDeferred(logger, order.Id, ex);
        }
    }

    [LoggerMessage(
        EventId = 3820,
        Level = LogLevel.Warning,
        Message = "The kernel's target ledger could not release the targets of order {OrderId} now; the staged OrderStateChangedV1 releases them when the kernel consumes it.")]
    private static partial void LogReleaseDeferred(ILogger logger, string orderId, Exception exception);
}
