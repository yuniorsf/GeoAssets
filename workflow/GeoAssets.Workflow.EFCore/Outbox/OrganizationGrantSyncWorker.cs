using GeoAssets.Identity.Authorization;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Workflow.Persistence.Outbox;

/// <summary>
/// Background worker (XD01-290) that keeps <see cref="OrganizationGrantMirrorRecord"/> current
/// by polling <see cref="IOrganizationGrantSyncClient"/> for grant changes recorded after the
/// locally-persisted <see cref="OrganizationGrantSyncCursorRecord"/>, upserting each batch into
/// the isolated <c>geoassets_fieldservice</c> database inside one transaction, then advancing the
/// cursor — the mechanism that lets <c>ServiceOrders</c>' grant-aware Row-Level-Security
/// exception keep working once that table can no longer reach the real <c>OrganizationGrants</c>
/// table directly.
///
/// Deliberately a simple poll-and-upsert loop, not a lease/claim/retry pipeline like
/// <see cref="OutboxRelayProcessor"/> — every sync entry is a full, idempotent snapshot
/// (upsert-by-<see cref="OrganizationGrantMirrorRecord.GrantId"/>), so a failed batch is safe to
/// just retry wholesale on the next tick rather than needing per-message dispatch tracking.
/// </summary>
public sealed partial class OrganizationGrantSyncWorker(
    IServiceScopeFactory scopeFactory,
    IOrganizationGrantSyncClient syncClient,
    TimeProvider timeProvider,
    IOptions<OrganizationGrantSyncOptions> options,
    ILogger<OrganizationGrantSyncWorker> logger) : BackgroundService
{
    private readonly OrganizationGrantSyncOptions _options = options.Value
        ?? throw new InvalidOperationException("OrganizationGrantSyncOptions value cannot be null.");

    /// <summary>Runs one poll/upsert/advance-cursor cycle. Returns the number of entries applied.</summary>
    public async Task<int> SyncNextBatchAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>();

            var cursor = await db.OrganizationGrantSyncCursor.SingleOrDefaultAsync(c => c.Id == 1, ct).ConfigureAwait(false);
            var afterSequence = cursor?.LastSequenceNumber ?? 0;

            var batch = await syncClient.GetSyncBatchAsync(afterSequence, _options.BatchSize, ct).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                return 0;
            }

            foreach (var entry in batch)
            {
                var mirror = await db.OrganizationGrantMirror.FindAsync([entry.GrantId], ct).ConfigureAwait(false);
                if (mirror is null)
                {
                    mirror = new OrganizationGrantMirrorRecord { GrantId = entry.GrantId };
                    db.OrganizationGrantMirror.Add(mirror);
                }

                mirror.GranteeOrganizationId = entry.GranteeOrganizationId;
                mirror.ResourceOrganizationId = entry.ResourceOrganizationId;
                mirror.ResourceType = entry.ResourceType;
                mirror.ScopeId = entry.ScopeId;
                mirror.IsActive = entry.IsActive;
                mirror.ExpiresAt = entry.ExpiresAt;
                mirror.SyncedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            }

            var newSequence = batch[^1].SequenceNumber;
            if (cursor is null)
            {
                db.OrganizationGrantSyncCursor.Add(new OrganizationGrantSyncCursorRecord
                {
                    LastSequenceNumber = newSequence,
                    UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
                });
            }
            else
            {
                cursor.LastSequenceNumber = newSequence;
                cursor.UpdatedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return batch.Count;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableSyncWorker)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var applied = await SyncNextBatchAsync(stoppingToken).ConfigureAwait(false);
                if (applied == 0)
                {
                    await Task.Delay(_options.PollInterval, timeProvider, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                LogSyncLoopError(logger, _options.PollInterval.TotalMilliseconds, ex);
                try
                {
                    await Task.Delay(_options.PollInterval, timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "OrganizationGrant sync loop encountered an unexpected error; retrying in {RetryDelayMs} ms.")]
    private static partial void LogSyncLoopError(ILogger logger, double retryDelayMs, Exception ex);
}
