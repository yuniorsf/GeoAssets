namespace GeoAssets.NeutralOperator.Application.Occupancy;

/// <summary>
/// Closes the staged occupancy change sets of an order that is closing (XD01-315, XD01-362): marks them discarded at
/// <c>closedAt</c> inside the transaction the order's closing transition runs in, so they are committed with the order's
/// closing status — and rolled back with it when that save fails. Sets already committed or discarded are not touched.
/// </summary>
public interface IOccupancyChangeSetCloser
{
    Task DiscardStagedAsync(string orderId, DateTimeOffset closedAt, CancellationToken cancellationToken);
}
