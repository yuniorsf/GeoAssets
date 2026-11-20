namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>What handling one reply did, and — when it ended a saga — that saga as it ended.</summary>
public sealed record OccupancyApplySagaResult(OccupancyApplySagaOutcome Outcome, OccupancyApplySaga? Saga = null)
{
    public static OccupancyApplySagaResult Ignored { get; } = new(OccupancyApplySagaOutcome.Ignored);

    public static OccupancyApplySagaResult Duplicate { get; } = new(OccupancyApplySagaOutcome.Duplicate);
}
