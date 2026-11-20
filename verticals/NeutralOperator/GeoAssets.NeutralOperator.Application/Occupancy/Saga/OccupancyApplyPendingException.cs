namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>
/// An approval refused because an earlier approval's asset-mutation apply is still open (XD01-383): the order itself awaits
/// the kernel's apply, or one of its poles is held by another order's open saga — writing it now would make a rejected
/// batch impossible to compensate.
/// </summary>
public sealed class OccupancyApplyPendingException : InvalidOperationException
{
    public OccupancyApplyPendingException(string orderId, IReadOnlyList<string> poleFeatureIds)
        : base(MessageFor(orderId, poleFeatureIds ?? throw new ArgumentNullException(nameof(poleFeatureIds))))
    {
        OrderId = orderId;
        PoleFeatureIds = poleFeatureIds;
    }

    public OccupancyApplyPendingException()
    {
        OrderId = string.Empty;
        PoleFeatureIds = [];
    }

    public OccupancyApplyPendingException(string message)
        : base(message)
    {
        OrderId = string.Empty;
        PoleFeatureIds = [];
    }

    public OccupancyApplyPendingException(string message, Exception innerException)
        : base(message, innerException)
    {
        OrderId = string.Empty;
        PoleFeatureIds = [];
    }

    public string OrderId { get; }

    /// <summary>The poles another order's open saga holds; empty when it is this order that awaits the apply.</summary>
    public IReadOnlyList<string> PoleFeatureIds { get; }

    private static string MessageFor(string orderId, IReadOnlyList<string> poleFeatureIds) => poleFeatureIds.Count == 0
        ? $"Service order '{orderId}' is approved and awaits the asset catalog's apply."
        : $"The occupancy of pole(s) {string.Join(", ", poleFeatureIds)} awaits the asset catalog's apply of another approval.";
}
