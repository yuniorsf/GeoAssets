namespace GeoAssets.Workflow.Targets;

/// <summary>
/// Declares that an order of the owning type is split into one child order per party when it
/// is dispatched.
/// </summary>
/// <param name="ChildOrderTypeId">
/// <see cref="Orders.OrderType.Id"/> of the child orders. Must not be blank and must differ from
/// the owning order type's id (see <see cref="OrderTargetPolicy.Validate"/>).
/// </param>
/// <param name="PartyKind">Which party each child order is addressed to.</param>
public sealed record OrderTargetFanOutPolicy(
    string ChildOrderTypeId,
    OrderTargetFanOutPartyKind PartyKind = OrderTargetFanOutPartyKind.OccupantOrganization);
