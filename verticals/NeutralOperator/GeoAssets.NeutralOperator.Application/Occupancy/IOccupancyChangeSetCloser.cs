namespace GeoAssets.NeutralOperator.Application.Occupancy;

/// <summary>
/// Closes the staged occupancy change sets of an order that is closing (XD01-315, XD01-362): marks them discarded at
/// <c>closedAt</c> in the caller's unit of work <b>without saving</b>, so they are written by the same <c>SaveChanges</c> as the
/// order's closing status — and undone with it when that save fails. Sets already committed or discarded are not touched.
/// </summary>
public interface IOccupancyChangeSetCloser
{
    Task DiscardStagedAsync(string orderId, DateTimeOffset closedAt, CancellationToken cancellationToken);
}
