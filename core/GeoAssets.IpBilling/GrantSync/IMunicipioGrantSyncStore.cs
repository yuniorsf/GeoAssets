namespace GeoAssets.IpBilling.GrantSync;

/// <summary>
/// The dispatcher side of the município grant-sync outbox (XD01-395). Requests of one município are sent strictly in the
/// order they were queued and never two at a time: only the oldest unfinished request of a município can be claimed, and
/// claiming takes a lease (<paramref name="workerId"/> until now + lease) that only one worker can hold. Every outcome is
/// recorded only by the lease holder; a lease that expires (a crashed worker) makes the request claimable again — the
/// kernel's grant upsert and revoke are idempotent, so sending it twice is harmless.
/// </summary>
public interface IMunicipioGrantSyncStore
{
    /// <summary>Claims up to <paramref name="batchSize"/> due requests, oldest first, at most one per município.</summary>
    Task<IReadOnlyList<MunicipioGrantSyncWorkItem>> ClaimDueAsync(
        int batchSize, TimeSpan leaseDuration, string workerId, CancellationToken ct = default);

    /// <summary>Marks the request sent; false when <paramref name="workerId"/> no longer holds its lease.</summary>
    Task<bool> MarkCompletedAsync(long id, string workerId, CancellationToken ct = default);

    /// <summary>Counts a failed attempt and schedules the next at <paramref name="nextAttemptAt"/>; false when the lease is lost.</summary>
    Task<bool> ScheduleRetryAsync(long id, string workerId, string failure, DateTimeOffset nextAttemptAt, CancellationToken ct = default);

    /// <summary>Counts a failed attempt and gives up on the request (it stays, Failed); false when the lease is lost.</summary>
    Task<bool> MarkFailedAsync(long id, string workerId, string failure, CancellationToken ct = default);

    /// <summary>
    /// Deletes up to <paramref name="batchSize"/> finished requests that are not their município's latest (XD01-400), oldest
    /// first: completed before <paramref name="completedBefore"/>, or — only when <paramref name="failedBefore"/> is set —
    /// failed before it. A município's latest request (which its <c>grantSyncState</c> is derived from) and unfinished
    /// requests are never deleted. Returns how many rows it deleted.
    /// </summary>
    Task<int> PruneAsync(DateTimeOffset completedBefore, DateTimeOffset? failedBefore, int batchSize, CancellationToken ct = default);
}
