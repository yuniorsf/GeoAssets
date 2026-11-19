namespace GeoAssets.Workflow.Targets;

/// <summary>
/// What the kernel knows of an order whose targets it keeps in the global ledger without holding the order itself
/// (XD01-379): a remote vertical's order, described by the reserving request and by the order type the vertical
/// published to the descriptor registry. Enough to select, evaluate conflicts and write targets — the part of
/// <see cref="OrderTargetSelectionService"/> that never needed the whole <see cref="Orders.IServiceOrder"/>.
/// </summary>
public sealed record TargetLedgerOrder
{
    public TargetLedgerOrder(string verticalId, string orderId, string orderTypeId, Guid organizationId, OrderTargetPolicy policy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(verticalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderId);
        ArgumentException.ThrowIfNullOrWhiteSpace(orderTypeId);
        ArgumentNullException.ThrowIfNull(policy);

        VerticalId = verticalId;
        OrderId = orderId;
        OrderTypeId = orderTypeId;
        OrganizationId = organizationId;
        Policy = policy;
    }

    /// <summary>The vertical that owns the order.</summary>
    public string VerticalId { get; }

    /// <summary>The order's id within <see cref="VerticalId"/>.</summary>
    public string OrderId { get; }

    /// <summary>The order's type, as the vertical published it.</summary>
    public string OrderTypeId { get; }

    /// <summary>The organization the order belongs to; every target is written under it.</summary>
    public Guid OrganizationId { get; }

    /// <summary>The target policy of <see cref="OrderTypeId"/>.</summary>
    public OrderTargetPolicy Policy { get; }

    /// <summary>The ledger key of the order.</summary>
    public TargetLedgerKey Key => new(VerticalId, OrderId);
}
