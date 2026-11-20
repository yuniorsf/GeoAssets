using GeoAssets.Workflow.Orders;

namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>The order as an approval or its saga stores it (XD01-318/XD01-383): a copy with a status and attributes of its own.</summary>
internal static class OccupancyOrderRevision
{
    /// <summary>
    /// <paramref name="order"/> moved to <paramref name="status"/> with <paramref name="attributes"/>, stamped at
    /// <paramref name="now"/>, carrying the row version it was read with; completed at <paramref name="now"/> when the status
    /// is the type's success state.
    /// </summary>
    public static ServiceOrder With(
        ServiceOrder order, OrderType? orderType, string status, IReadOnlyDictionary<string, string> attributes, DateTimeOffset now) => new()
        {
            Id = order.Id,
            RowVersion = order.RowVersion,
            Title = order.Title,
            Description = order.Description,
            OrderTypeId = order.OrderTypeId,
            Status = status,
            Priority = order.Priority,
            CreatedBy = order.CreatedBy,
            AssignedTo = order.AssignedTo,
            OrganizationId = order.OrganizationId,
            CreatedAt = order.CreatedAt,
            UpdatedAt = now.UtcDateTime,
            ScheduledAt = order.ScheduledAt,
            CompletedAt = !string.Equals(status, order.Status, StringComparison.Ordinal) && ServiceOrderTransitions.IsSuccessState(orderType, status)
            ? now.UtcDateTime
            : order.CompletedAt,
            Attributes = new Dictionary<string, string>(attributes, StringComparer.Ordinal),
            Features = [.. order.Features],
            SelectionSpec = order.SelectionSpec,
            FeatureIds = [.. order.FeatureIds],
            ParentOrderId = order.ParentOrderId,
            ChildOrderIds = [.. order.ChildOrderIds],
            Dispatches = [.. order.Dispatches],
            ActionLog = [.. order.ActionLog],
        };

    /// <summary>
    /// <paramref name="order"/>'s attributes with the saga's state (and, when given, its batch) set, and
    /// <see cref="OccupancyApplySagaAttributes.UnrestoredKey"/> naming <paramref name="unrestored"/> — removed when none is
    /// given, so it always describes the order's latest saga.
    /// </summary>
    public static Dictionary<string, string> SagaAttributes(
        IServiceOrder order, string state, Guid? batchId = null, IReadOnlyList<string>? unrestored = null)
    {
        var attributes = new Dictionary<string, string>(order.Attributes, StringComparer.Ordinal)
        {
            [OccupancyApplySagaAttributes.StateKey] = state,
        };
        if (batchId is { } batch)
            attributes[OccupancyApplySagaAttributes.BatchIdKey] = batch.ToString("D");
        if (unrestored is { Count: > 0 })
            attributes[OccupancyApplySagaAttributes.UnrestoredKey] = string.Join(',', unrestored);
        else
            attributes.Remove(OccupancyApplySagaAttributes.UnrestoredKey);
        return attributes;
    }
}
