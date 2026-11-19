namespace GeoAssets.Workflow.Persistence.Entities;

/// <summary>
/// EF entity for the <c>OrderTypeRecipientActions</c> table (XD01-336): one action an order type
/// lets a recipient of its orders take (<see cref="Orders.OrderType.RecipientActions"/>), and the
/// move that action makes. Each granted action has one row with no move, plus one row per
/// transition of the type tagged with it (<see cref="Orders.ServiceOrderTransitions.TransitionsFor"/>).
///
/// Keyed by the type's id with no foreign key to <c>OrderTypes</c>: a type registered only in code
/// (the shared-use notice) has no row there, and the row-level security of <c>ServiceOrders</c> and
/// <c>OrderActionLogs</c> still has to know what its recipients may do. Those rows are seeded by the
/// migration that created the table; a type stored through <see cref="EFOrderTypeRepository"/>
/// writes its own. The actions and state keys are compared by SQL, so <see cref="Action"/> is the
/// historical number (<c>OrderActionAliases</c>), never renumbered, and <see cref="ActionCode"/> the code (XD01-366).
/// </summary>
internal sealed class OrderTypeRecipientActionRecord
{
    public int    Id          { get; set; }
    public string OrderTypeId { get; set; } = string.Empty;

    /// <summary>The action's historical number (XD01-366: null for an action that has none — see <c>StoredOrderAction</c>).</summary>
    public int?   Action      { get; set; }

    /// <summary>The action's code (XD01-366), compared by the row-level security functions next to the number.</summary>
    public string? ActionCode { get; set; }

    /// <summary>The state the action moves the order from; null on the row that only grants the action.</summary>
    public string? FromStateKey { get; set; }

    /// <summary>The state the action moves the order to; null exactly when <see cref="FromStateKey"/> is.</summary>
    public string? ToStateKey   { get; set; }
}
