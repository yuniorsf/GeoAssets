namespace GeoAssets.NeutralOperator.Persistence.Entities;

/// <summary>
/// EF Core entity mapping to <c>occupancy_apply_sagas</c> (XD01-383): one occupancy approval's asset-mutation apply saga,
/// keyed by its batch. Ids, states, instants and the batch as staged — no occupancy entry: the poles' snapshots are in
/// <see cref="OccupancyApplySagaPoleRecord"/>, behind the tenant policy.
/// </summary>
internal sealed class OccupancyApplySagaRecord
{
    public Guid BatchId { get; set; }

    public string ServiceOrderId { get; set; } = string.Empty;

    public string OrderTypeId { get; set; } = string.Empty;

    public int Attempt { get; set; }

    public string PreviousStatus { get; set; } = string.Empty;

    public string ApprovedStatus { get; set; } = string.Empty;

    public string RequestedBy { get; set; } = string.Empty;

    public DateTime RequestedAt { get; set; }

    public DateTime LastRequestedAt { get; set; }

    public int RetryCount { get; set; }

    public DateTime? TimedOutAt { get; set; }

    /// <summary><c>OccupancyApplySagaState</c> by member name.</summary>
    public string State { get; set; } = string.Empty;

    public DateTime? CompletedAt { get; set; }

    public string? ReasonCode { get; set; }

    public string? Reason { get; set; }

    /// <summary>JSON array of the feature ids the kernel could not apply the batch to.</summary>
    public string ConflictingFeatureIdsJson { get; set; } = "[]";

    /// <summary>The <c>AssetMutationBatchRequestedEvent</c> as staged, as JSON: what a retry stages again.</summary>
    public string BatchJson { get; set; } = "{}";

    /// <summary>The order's organization — the pole owner.</summary>
    public Guid OrganizationId { get; set; } = Guid.Empty;
}
