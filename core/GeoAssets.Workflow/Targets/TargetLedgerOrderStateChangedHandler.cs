using GeoAssets.Core.Messaging;
using GeoAssets.Workflow.Notifications;

namespace GeoAssets.Workflow.Targets;

/// <summary>
/// Consumes the verticals' <c>OrderStateChangedV1</c> (XD01-379, ADR-001 §3.4): a remote vertical's order entering a
/// terminal state releases its targets in the global ledger, exactly once — see
/// <see cref="TargetLedgerService.ReleaseTerminalAsync"/>. This replaces, for remote verticals, the in-process release
/// that runs when an in-process order's status is written; events about in-process orders are ignored here.
/// </summary>
public sealed class TargetLedgerOrderStateChangedHandler : IIntegrationEventHandler<OrderStateChangedEvent>
{
    private readonly TargetLedgerService _ledger;

    public TargetLedgerOrderStateChangedHandler(TargetLedgerService ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        _ledger = ledger;
    }

    public Task HandleAsync(OrderStateChangedEvent message, IntegrationMessageContext context, CancellationToken ct = default) =>
        _ledger.ReleaseTerminalAsync(message, context, ct);
}
