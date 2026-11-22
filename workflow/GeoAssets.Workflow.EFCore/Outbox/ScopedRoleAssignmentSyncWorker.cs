using System.Text.Json;
using GeoAssets.Identity.Authorization;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Workflow.Persistence.Outbox;

/// <summary>
/// Background worker (XD01-328) that keeps <see cref="ScopedRoleAssignmentMirrorRecord"/> current by polling
/// <see cref="IScopedRoleAssignmentSyncClient"/> for changes recorded after the locally persisted
/// <see cref="ScopedRoleAssignmentSyncCursorRecord"/> — the twin of <see cref="OrganizationGrantSyncWorker"/>, with the same
/// options section (<see cref="OrganizationGrantSyncOptions"/>: batch size, poll interval, on/off) and the same staleness
/// bound: a role revoked on the Core Platform can still count here for up to about one
/// <see cref="OrganizationGrantSyncOptions.PollInterval"/>.
///
/// Each entry is the pair's whole state: an active one replaces the row, a tombstone deletes it. A batch is applied and
/// the cursor advanced in one transaction, so a failed batch is simply retried on the next tick.
/// </summary>
public sealed partial class ScopedRoleAssignmentSyncWorker(
    IServiceScopeFactory scopeFactory,
    IScopedRoleAssignmentSyncClient syncClient,
    TimeProvider timeProvider,
    IOptions<OrganizationGrantSyncOptions> options,
    ILogger<ScopedRoleAssignmentSyncWorker> logger) : BackgroundService
{
    private readonly OrganizationGrantSyncOptions _options = options.Value
        ?? throw new InvalidOperationException("OrganizationGrantSyncOptions value cannot be null.");

    /// <summary>Runs one poll/apply/advance-cursor cycle. Returns the number of entries applied.</summary>
    public async Task<int> SyncNextBatchAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var scope = scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceOrderDbContext>();

            var cursor = await db.ScopedRoleAssignmentSyncCursor.SingleOrDefaultAsync(c => c.Id == 1, ct).ConfigureAwait(false);
            var afterSequence = cursor?.LastSequenceNumber ?? 0;

            var batch = await syncClient.GetSyncBatchAsync(afterSequence, _options.BatchSize, ct).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                return 0;
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;

            // Every entry is the pair's whole state, so only the last one of each pair in the batch matters.
            var latest = batch
                .GroupBy(e => (e.UserId, e.ResourceOrganizationId))
                .Select(g => g.MaxBy(e => e.SequenceNumber)!);

            foreach (var entry in latest)
                await ApplyAsync(db, entry, now, ct).ConfigureAwait(false);

            var newSequence = batch[^1].SequenceNumber;
            if (cursor is null)
            {
                db.ScopedRoleAssignmentSyncCursor.Add(new ScopedRoleAssignmentSyncCursorRecord { LastSequenceNumber = newSequence, UpdatedAtUtc = now });
            }
            else
            {
                cursor.LastSequenceNumber = newSequence;
                cursor.UpdatedAtUtc = now;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return batch.Count;
        }
    }

    /// <summary>Replaces the pair's row with an active entry, or deletes it for a tombstone.</summary>
    private static async Task ApplyAsync(ServiceOrderDbContext db, ScopedRoleAssignmentSyncEntry entry, DateTime now, CancellationToken ct)
    {
        var mirror = await db.ScopedRoleAssignmentMirror
            .FindAsync([entry.UserId, entry.ResourceOrganizationId], ct).ConfigureAwait(false);

        if (!entry.IsActive)
        {
            if (mirror is not null)
                db.ScopedRoleAssignmentMirror.Remove(mirror);
            return;
        }

        if (mirror is null)
        {
            mirror = new ScopedRoleAssignmentMirrorRecord { UserId = entry.UserId, ResourceOrganizationId = entry.ResourceOrganizationId };
            db.ScopedRoleAssignmentMirror.Add(mirror);
        }

        mirror.ExternalObjectId = entry.ExternalObjectId;
        mirror.RoleNamesJson = JsonSerializer.Serialize(entry.RoleNames);
        mirror.PermissionCodesJson = JsonSerializer.Serialize(entry.PermissionCodes);
        mirror.SyncedAtUtc = now;
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
        Message = "Scoped role assignment sync loop encountered an unexpected error; retrying in {RetryDelayMs} ms.")]
    private static partial void LogSyncLoopError(ILogger logger, double retryDelayMs, Exception ex);
}
