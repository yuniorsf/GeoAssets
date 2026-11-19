using System.Text.Json;
using GeoAssets.Workflow.Targets;

namespace GeoAssets.Workflow.Registry;

/// <summary>
/// What a vertical publishes about one of its order types (XD01-369, the order-type chassis contract): data, never code —
/// the kernel resolves its <see cref="FeatureActions"/> against asset-type roles without running the vertical.
/// </summary>
public sealed record OrderTypeDescriptor
{
    public required string VerticalId { get; init; }
    public required string OrderTypeId { get; init; }
    public required string DisplayNameKey { get; init; }

    /// <summary>The base route of the vertical's order API, e.g. <c>/api/workflow/service-orders</c>.</summary>
    public required string ApiBaseRoute { get; init; }

    public IReadOnlyList<FeatureActionDescriptor> FeatureActions { get; init => field = value ?? []; } = [];

    /// <summary>
    /// What orders of this type may target, when they reserve targets in the kernel's global target ledger (XD01-379):
    /// validated as <see cref="Orders.OrderTypeRegistry"/> validates an in-process type's
    /// <see cref="Orders.OrderType.TargetPolicy"/>. Null for a type whose orders hold no targets.
    /// </summary>
    public OrderTargetPolicy? TargetPolicy { get; init; }

    /// <summary>
    /// The statuses in which an order of this type is closed (XD01-379). The ledger releases a vertical's order's targets
    /// when the vertical reports the order entering one of them and its event does not say itself whether the new
    /// status is terminal. Compared ordinally.
    /// </summary>
    public IReadOnlyList<string> TerminalStatuses { get; init => field = value ?? []; } = [];

    /// <summary>Structural equality: the same fields, the same feature actions and terminal statuses, in order, and the same target policy.</summary>
    public bool SameAs(OrderTypeDescriptor other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return string.Equals(VerticalId, other.VerticalId, StringComparison.Ordinal)
            && string.Equals(OrderTypeId, other.OrderTypeId, StringComparison.Ordinal)
            && string.Equals(DisplayNameKey, other.DisplayNameKey, StringComparison.Ordinal)
            && string.Equals(ApiBaseRoute, other.ApiBaseRoute, StringComparison.Ordinal)
            && FeatureActions.SequenceEqual(other.FeatureActions)
            && TerminalStatuses.SequenceEqual(other.TerminalStatuses, StringComparer.Ordinal)
            && SamePolicy(TargetPolicy, other.TargetPolicy);
    }

    /// <summary>A policy holds lists, whose record equality is by reference; two policies are the same when they serialize alike.</summary>
    private static bool SamePolicy(OrderTargetPolicy? left, OrderTargetPolicy? right) =>
        ReferenceEquals(left, right)
        || (left is not null && right is not null
            && string.Equals(JsonSerializer.Serialize(left), JsonSerializer.Serialize(right), StringComparison.Ordinal));
}
