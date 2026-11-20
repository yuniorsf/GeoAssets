using GeoAssets.Core.Messaging.Events;

namespace GeoAssets.NeutralOperator.Application.Occupancy.Saga;

/// <summary>
/// One occupancy approval waiting for — or answered by — the kernel's apply of its asset mutation batch (XD01-383, ADR-001
/// Phase 7). Keyed by the batch: the kernel's reply names only the batch, and the batch is what an operator's retry stages
/// again, unchanged.
/// </summary>
public sealed record OccupancyApplySaga
{
    /// <summary>Longest <see cref="ReasonCode"/> a store accepts.</summary>
    public const int MaxReasonCodeLength = 64;

    /// <summary>Longest <see cref="Reason"/> a store keeps; a longer one is cut.</summary>
    public const int MaxReasonLength = 2000;

    /// <summary>The asset mutation batch the approval staged, and this saga's identity.</summary>
    public required Guid BatchId { get; init; }

    /// <summary>Which approval of the order this is: 1 for the first, one more after each rejected one.</summary>
    public required int Attempt { get; init; }

    public required string OrderId { get; init; }

    public required string OrderTypeId { get; init; }

    /// <summary>The order's organization: the pole owner.</summary>
    public required Guid OrganizationId { get; init; }

    /// <summary>The order's status when it was approved — where it stays until the kernel answers, and where a rejection leaves it.</summary>
    public required string PreviousStatus { get; init; }

    /// <summary>The status the approval leads to, entered when the kernel applies the batch.</summary>
    public required string ApprovedStatus { get; init; }

    /// <summary>User id of the approver.</summary>
    public required string RequestedBy { get; init; }

    /// <summary>When the approval staged the batch.</summary>
    public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>When the batch was last staged: the approval, or the last operator retry. The saga timeout counts from it.</summary>
    public required DateTimeOffset LastRequestedAt { get; init; }

    /// <summary>How many times an operator re-staged the batch.</summary>
    public int RetryCount { get; init; }

    /// <summary>When the timeout sweeper found the saga still pending past the timeout since <see cref="LastRequestedAt"/>; null otherwise.</summary>
    public DateTimeOffset? TimedOutAt { get; init; }

    public required OccupancyApplySagaState State { get; init; }

    /// <summary>When the kernel's reply ended the saga; null while pending.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>The kernel's <see cref="AssetMutationBatchRejectionCodes"/> code, for a reverted saga.</summary>
    public string? ReasonCode { get; init; }

    /// <summary>The kernel's reason, for a reverted saga.</summary>
    public string? Reason { get; init; }

    /// <summary>The features the kernel could not apply the batch to, for a reverted saga; may be empty.</summary>
    public IReadOnlyList<string> ConflictingFeatureIds { get; init; } = [];

    /// <summary>The batch exactly as staged — what a retry stages again.</summary>
    public required AssetMutationBatchRequestedEvent Batch { get; init; }
}
