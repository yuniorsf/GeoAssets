using GeoAssets.Core.Messaging;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence.Outbox;

/// <summary>
/// EF Core idempotent inbox tracking processed <c>(MessageId, ConsumerName)</c> pairs.
/// </summary>
public sealed class EFIdempotentInbox : IIdempotentInbox
{
    private readonly ServiceOrderDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EFIdempotentInbox(ServiceOrderDbContext db, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _db = db;
        _timeProvider = timeProvider;
    }

    public Task<bool> HasProcessedAsync(
        Guid messageId,
        string consumerName,
        CancellationToken ct = default)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("Message ID cannot be Guid.Empty.", nameof(messageId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ct.ThrowIfCancellationRequested();

        return _db.InboxProcessedMessages
            .AnyAsync(x => x.MessageId == messageId && x.ConsumerName == consumerName, ct);
    }

    public async Task<bool> TryRecordProcessedAsync(
        Guid messageId,
        string consumerName,
        string messageType,
        TimeSpan retentionPeriod,
        CancellationToken ct = default)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("Message ID cannot be Guid.Empty.", nameof(messageId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(consumerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(retentionPeriod, TimeSpan.Zero);
        ct.ThrowIfCancellationRequested();

        if (await HasProcessedAsync(messageId, consumerName, ct).ConfigureAwait(false))
        {
            return false;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var record = new InboxProcessedMessageRecord
        {
            MessageId = messageId,
            ConsumerName = consumerName,
            MessageType = messageType,
            ProcessedAtUtc = nowUtc,
            ExpiresAtUtc = nowUtc.Add(retentionPeriod),
        };

        _db.InboxProcessedMessages.Add(record);
        try
        {
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }
        catch (DbUpdateException)
        {
            _db.Entry(record).State = EntityState.Detached;
            return false;
        }
    }

    public async Task<int> PurgeExpiredAsync(int maxBatchSize, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBatchSize);
        ct.ThrowIfCancellationRequested();

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var expired = await _db.InboxProcessedMessages
            .Where(x => x.ExpiresAtUtc <= nowUtc)
            .OrderBy(x => x.ExpiresAtUtc)
            .Take(maxBatchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (expired.Count == 0)
        {
            return 0;
        }

        _db.InboxProcessedMessages.RemoveRange(expired);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return expired.Count;
    }
}
