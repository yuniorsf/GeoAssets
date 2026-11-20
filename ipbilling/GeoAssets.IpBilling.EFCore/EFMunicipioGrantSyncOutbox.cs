using GeoAssets.IpBilling.GrantSync;
using GeoAssets.IpBilling.Persistence.Configurations;
using GeoAssets.IpBilling.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.IpBilling.Persistence;

/// <summary>
/// EF Core implementation of the município grant-sync outbox (XD01-395) over <see cref="IpBillingDbContext"/>'s
/// <c>MunicipioGrantSyncRequests</c>. <see cref="Enqueue"/> only stages the row on the scoped context, which
/// <see cref="EFMunicipioRepository"/> shares, so the município save's <c>SaveChangesAsync</c> commits both. The dispatcher
/// side claims with a lease taken by a conditional <c>UPDATE</c> (only one worker can win it, on any provider), recording
/// every outcome only while it still holds the lease.
/// </summary>
public sealed class EFMunicipioGrantSyncOutbox(IpBillingDbContext db, TimeProvider timeProvider)
    : IMunicipioGrantSyncQueue, IMunicipioGrantSyncStore
{
    public void Enqueue(MunicipioGrantChange change, string requestedBy)
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(change.IbgeCode);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        db.MunicipioGrantSyncRequests.Add(new MunicipioGrantSyncRequestRecord
        {
            MunicipioId = change.MunicipioId,
            Kind = change.Kind,
            IbgeCode = change.IbgeCode,
            DistribuidoraOrganizationId = change.DistribuidoraOrganizationId,
            PrefeituraOrganizationId = change.PrefeituraOrganizationId,
            PreviousPrefeituraOrganizationId = change.PreviousPrefeituraOrganizationId,
            PreviousIbgeCode = change.PreviousIbgeCode,
            PreviousDistribuidoraOrganizationId = change.PreviousDistribuidoraOrganizationId,
            RequestedBy = Bound(requestedBy, MunicipioGrantSyncRequestRecordConfiguration.MaxRequestedByLength),
            RequestedAt = now,
            NextAttemptAt = now,
        });
    }

    public async Task<IReadOnlyList<MunicipioGrantSyncWorkItem>> ClaimDueAsync(
        int batchSize, TimeSpan leaseDuration, string workerId, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var leaseUntil = now.Add(leaseDuration);
        var worker = Bound(workerId, MunicipioGrantSyncRequestRecordConfiguration.MaxWorkerIdLength);
        var requests = db.MunicipioGrantSyncRequests;

        // Only the head of each município's line (its oldest unfinished request) is a candidate, so a later request never
        // overtakes, nor runs beside, an earlier one still in flight or waiting for its retry.
        var candidates = await requests
            .AsNoTracking()
            .Where(r => r.CompletedAt == null && r.FailedAt == null
                && r.NextAttemptAt <= now
                && (r.LeaseExpiresAt == null || r.LeaseExpiresAt <= now)
                && !requests.Any(o => o.MunicipioId == r.MunicipioId
                    && o.CompletedAt == null && o.FailedAt == null
                    && o.Id < r.Id))
            .OrderBy(r => r.Id)
            .Take(batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var claimed = new List<MunicipioGrantSyncWorkItem>(candidates.Count);
        foreach (var candidate in candidates)
        {
            var won = await requests
                .Where(r => r.Id == candidate.Id
                    && r.CompletedAt == null && r.FailedAt == null
                    && (r.LeaseExpiresAt == null || r.LeaseExpiresAt <= now))
                .ExecuteUpdateAsync(
                    s => s.SetProperty(r => r.LeasedBy, worker).SetProperty(r => r.LeaseExpiresAt, leaseUntil),
                    ct)
                .ConfigureAwait(false);

            if (won == 1) claimed.Add(ToWorkItem(candidate));
        }

        return claimed;
    }

    public async Task<bool> MarkCompletedAsync(long id, string workerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var updated = await Leased(id, workerId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.CompletedAt, now)
                      .SetProperty(r => r.LeasedBy, (string?)null)
                      .SetProperty(r => r.LeaseExpiresAt, (DateTime?)null),
                ct)
            .ConfigureAwait(false);
        return updated == 1;
    }

    public async Task<bool> ScheduleRetryAsync(
        long id, string workerId, string failure, DateTimeOffset nextAttemptAt, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(failure);

        var lastError = Bound(failure, MunicipioGrantSyncRequestRecordConfiguration.MaxErrorLength);
        var next = nextAttemptAt.UtcDateTime;
        var updated = await Leased(id, workerId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Attempts, r => r.Attempts + 1)
                      .SetProperty(r => r.LastError, lastError)
                      .SetProperty(r => r.NextAttemptAt, next)
                      .SetProperty(r => r.LeasedBy, (string?)null)
                      .SetProperty(r => r.LeaseExpiresAt, (DateTime?)null),
                ct)
            .ConfigureAwait(false);
        return updated == 1;
    }

    public async Task<bool> MarkFailedAsync(long id, string workerId, string failure, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(failure);

        var lastError = Bound(failure, MunicipioGrantSyncRequestRecordConfiguration.MaxErrorLength);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var updated = await Leased(id, workerId)
            .ExecuteUpdateAsync(
                s => s.SetProperty(r => r.Attempts, r => r.Attempts + 1)
                      .SetProperty(r => r.LastError, lastError)
                      .SetProperty(r => r.FailedAt, now)
                      .SetProperty(r => r.LeasedBy, (string?)null)
                      .SetProperty(r => r.LeaseExpiresAt, (DateTime?)null),
                ct)
            .ConfigureAwait(false);
        return updated == 1;
    }

    private IQueryable<MunicipioGrantSyncRequestRecord> Leased(long id, string workerId)
    {
        var worker = Bound(workerId, MunicipioGrantSyncRequestRecordConfiguration.MaxWorkerIdLength);
        return db.MunicipioGrantSyncRequests.Where(r => r.Id == id
            && r.LeasedBy == worker
            && r.CompletedAt == null && r.FailedAt == null);
    }

    private static MunicipioGrantSyncWorkItem ToWorkItem(MunicipioGrantSyncRequestRecord r) => new(
        r.Id,
        new MunicipioGrantChange(
            r.MunicipioId,
            r.Kind,
            r.IbgeCode,
            r.DistribuidoraOrganizationId,
            r.PrefeituraOrganizationId,
            r.PreviousPrefeituraOrganizationId,
            r.PreviousIbgeCode,
            r.PreviousDistribuidoraOrganizationId),
        r.RequestedBy,
        new DateTimeOffset(DateTime.SpecifyKind(r.RequestedAt, DateTimeKind.Utc)),
        r.Attempts);

    private static string Bound(string value, int maxLength) =>
        value.Length > maxLength ? value[..maxLength] : value;
}
