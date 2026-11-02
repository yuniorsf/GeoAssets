using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.Workflow.FieldWork;

/// <summary>Outcome of <see cref="CtoOccupancyMutationFactory.BuildAdjustmentMutation"/> (XD01-281).</summary>
public sealed record CtoOccupancyAdjustmentResult(bool Succeeded, string? FailureReason, AssetFeatureMutationItem? Mutation, int? NewOccupiedCount)
{
    public static CtoOccupancyAdjustmentResult Success(AssetFeatureMutationItem mutation, int newOccupiedCount) =>
        new(true, null, mutation, newOccupiedCount);

    public static CtoOccupancyAdjustmentResult Failure(string reason) => new(false, reason, null, null);
}
