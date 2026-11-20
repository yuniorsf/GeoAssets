namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>What the retry route answers (XD01-383): the saga as it stands after the retry was staged.</summary>
public sealed record OccupancyApplySagaView(
    Guid BatchId,
    string OrderId,
    int Attempt,
    OccupancyApplySagaState State,
    DateTimeOffset RequestedAt,
    DateTimeOffset LastRequestedAt,
    int RetryCount,
    DateTimeOffset? TimedOutAt)
{
    public static OccupancyApplySagaView From(OccupancyApplySaga saga)
    {
        ArgumentNullException.ThrowIfNull(saga);
        return new(saga.BatchId, saga.OrderId, saga.Attempt, saga.State, saga.RequestedAt, saga.LastRequestedAt, saga.RetryCount, saga.TimedOutAt);
    }
}
