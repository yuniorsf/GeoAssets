using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Modules;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Tools.NeutralOperatorDataMove;

/// <summary>A NeutralOperator order type and the exclusivity its target policy declares (null: no target policy).</summary>
internal sealed record MovedOrderType(string Id, OrderTargetExclusivity? Exclusivity)
{
    /// <summary>The id as the statements compare it: <c>upper("OrderTypeId")</c>, the registry being case-insensitive.</summary>
    public string Key => Id.ToUpperInvariant();
}

/// <summary>
/// Which orders and messages move, derived from the code that defines them — never from a string list of this tool's own:
/// the order types are exactly the ones <see cref="NeutralOperatorModule.RegisterOrderTypes"/> registers (what both hosts
/// serve, and what NeutralOperator.Server publishes to the kernel's registry), the vertical id is the module's, and the
/// messages are the two integration events the module stages on its host's outbox.
/// </summary>
internal sealed class NeutralOperatorMoveScope
{
    private NeutralOperatorMoveScope(IReadOnlyList<MovedOrderType> orderTypes) => OrderTypes = orderTypes;

    /// <summary>The NeutralOperator order types, ordered by id.</summary>
    public IReadOnlyList<MovedOrderType> OrderTypes { get; }

    /// <summary><see cref="MovedOrderType.Key"/> of every order type.</summary>
    public IReadOnlyList<string> OrderTypeKeys => [.. OrderTypes.Select(t => t.Key)];

    /// <summary>The vertical the ledger keys a NeutralOperator.Server order under: <see cref="NeutralOperatorModule.ModuleId"/>.</summary>
    public static string VerticalId => NeutralOperatorModule.ModuleId;

    /// <summary>The vertical of an in-process order's targets — what the kernel's ledger holds the moved orders' targets under.</summary>
    public static string KernelVerticalId => TargetLedger.KernelVerticalId;

    /// <summary>
    /// The messages the module stages, partitioned by their order's id: the approval's asset mutation batch (and its
    /// retries) and the shared-use dispatch's notifications. The kernel's own answers to a batch
    /// (<see cref="IntegrationEventTypes.AssetMutationBatchAppliedV1"/>, <see cref="IntegrationEventTypes.AssetMutationBatchRejectedV1"/>)
    /// are the catalog's messages and stay in the kernel's outbox.
    /// </summary>
    public static IReadOnlyList<string> MessageTypes { get; } =
        [IntegrationEventTypes.AssetMutationBatchRequestedV1, IntegrationEventTypes.OrderStateChangedV1];

    /// <summary>The outbox status of a delivered message (<see cref="OutboxMessageStatus.Dispatched"/>): every other one is undelivered.</summary>
    public static int DispatchedStatus => (int)OutboxMessageStatus.Dispatched;

    public static NeutralOperatorMoveScope FromModule()
    {
        var registry = new OrderTypeRegistry();
        NeutralOperatorModule.RegisterOrderTypes(registry);

        var orderTypes = registry.All
            .Select(t => new MovedOrderType(t.Id, t.TargetPolicy?.Exclusivity))
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
        if (orderTypes.Count == 0)
            throw new InvalidOperationException("The NeutralOperator module registers no order type: there is nothing to move.");

        return new NeutralOperatorMoveScope(orderTypes);
    }
}
