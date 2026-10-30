using System.Diagnostics;
using System.Text.Json;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace GeoAssets.Workflow.Persistence.Outbox;

/// <summary>
/// EF Core transactional outbox implementation backing both <see cref="IOutboxWriter"/>
/// and <see cref="IOutboxDispatcherStore"/> on <see cref="ServiceOrderDbContext"/>.
/// </summary>
public sealed class EFOutboxStore : IOutboxWriter, IOutboxDispatcherStore
{
    private const int MaxErrorLength = 2048;

    private readonly ServiceOrderDbContext _db;
    private readonly TimeProvider _timeProvider;
    private readonly IIntegrationMessageSerializer? _serializer;
    private readonly string? _sourceService;

    public EFOutboxStore(
        ServiceOrderDbContext db,
        TimeProvider timeProvider,
        IIntegrationMessageSerializer? serializer = null,
        string? sourceService = null)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _db = db;
        _timeProvider = timeProvider;
        _serializer = serializer;
        _sourceService = sourceService;
    }

    public async Task<Guid> EnqueueAsync<TEvent>(
        TEvent message,
        string messageType,
        string partitionKey,
        string? catalogInstanceId = null,
        string? correlationId = null,
        string? causationId = null,
        CancellationToken ct = default)
        where TEvent : notnull
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        ct.ThrowIfCancellationRequested();

        if (_serializer is null)
        {
            throw new InvalidOperationException(
                "An IIntegrationMessageSerializer must be provided to EFOutboxStore to serialize typed integration events.");
        }

        var payload = _serializer.Serialize(message);
        var (traceParent, traceState) = CaptureCurrentTraceContext();

        var envelope = IntegrationEnvelope.Create(
            messageType: messageType,
            schemaVersion: IntegrationEventTypes.CurrentSchemaVersion,
            partitionKey: partitionKey,
            contentType: _serializer.ContentType,
            payload: payload,
            timeProvider: _timeProvider,
            catalogInstanceId: catalogInstanceId,
            sourceService: _sourceService,
            correlationId: correlationId,
            causationId: causationId,
            traceParent: traceParent,
            traceState: traceState);

        await EnqueueEnvelopeAsync(envelope, ct).ConfigureAwait(false);
        return envelope.MessageId;
    }

    public Task EnqueueEnvelopeAsync(IntegrationEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.MessageId == Guid.Empty)
        {
            throw new ArgumentException("Envelope MessageId cannot be Guid.Empty.", nameof(envelope));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.MessageType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(envelope.SchemaVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.PartitionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.ContentType);
        ct.ThrowIfCancellationRequested();

        var traceParent = envelope.TraceParent;
        var traceState = envelope.TraceState;
        if (traceParent is null && Activity.Current is not null)
        {
            var captured = CaptureCurrentTraceContext();
            traceParent = captured.TraceParent;
            traceState ??= captured.TraceState;
        }

        var occurredUtc = envelope.OccurredAtUtc.UtcDateTime;
        var record = new OutboxMessageRecord
        {
            Id = envelope.MessageId,
            MessageType = envelope.MessageType,
            SchemaVersion = envelope.SchemaVersion,
            PartitionKey = envelope.PartitionKey,
            ContentType = envelope.ContentType,
            Payload = envelope.Payload.ToArray(),
            CatalogInstanceId = envelope.CatalogInstanceId,
            SourceService = envelope.SourceService ?? _sourceService,
            CorrelationId = envelope.CorrelationId,
            CausationId = envelope.CausationId,
            TraceParent = traceParent,
            TraceState = traceState,
            HeadersJson = envelope.Headers is { Count: > 0 }
                ? JsonSerializer.Serialize(envelope.Headers)
                : null,
            Status = (int)OutboxMessageStatus.Pending,
            AttemptCount = 0,
            CreatedAtUtc = occurredUtc,
            NextRetryAtUtc = occurredUtc,
        };

        _db.OutboxMessages.Add(record);
        return _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<OutboxPendingMessage>> ClaimPendingBatchAsync(
        int batchSize,
        TimeSpan leaseDuration,
        string workerId,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(leaseDuration, TimeSpan.Zero);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ct.ThrowIfCancellationRequested();

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var pendingStatus = (int)OutboxMessageStatus.Pending;
        var processingStatus = (int)OutboxMessageStatus.Processing;

        var candidates = await _db.OutboxMessages
            .Where(x =>
                (x.Status == pendingStatus && x.NextRetryAtUtc <= nowUtc) ||
                (x.Status == processingStatus && x.LockedUntilUtc != null && x.LockedUntilUtc <= nowUtc))
            .OrderBy(x => x.CreatedAtUtc)
            .Take(batchSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return [];
        }

        var lockedUntil = nowUtc.Add(leaseDuration);
        var lockedUntilOffset = new DateTimeOffset(DateTime.SpecifyKind(lockedUntil, DateTimeKind.Utc));

        foreach (var row in candidates)
        {
            row.Status = processingStatus;
            row.LockedBy = workerId;
            row.LockedUntilUtc = lockedUntil;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        var claimed = new List<OutboxPendingMessage>(candidates.Count);
        foreach (var row in candidates)
        {
            claimed.Add(new OutboxPendingMessage(
                Envelope: MapToEnvelope(row),
                AttemptCount: row.AttemptCount,
                LockedUntilUtc: lockedUntilOffset));
        }

        return claimed;
    }

    public async Task MarkDispatchedAsync(Guid messageId, string workerId, CancellationToken ct = default)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("Message ID cannot be Guid.Empty.", nameof(messageId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ct.ThrowIfCancellationRequested();

        var row = await _db.OutboxMessages
            .FirstOrDefaultAsync(x => x.Id == messageId, ct)
            .ConfigureAwait(false);

        if (row is null ||
            row.Status != (int)OutboxMessageStatus.Processing ||
            !string.Equals(row.LockedBy, workerId, StringComparison.Ordinal))
        {
            return;
        }

        row.Status = (int)OutboxMessageStatus.Dispatched;
        row.DispatchedAtUtc = _timeProvider.GetUtcNow().UtcDateTime;
        row.LockedBy = null;
        row.LockedUntilUtc = null;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task MarkFailedAsync(
        Guid messageId,
        string workerId,
        string errorMessage,
        TimeSpan retryDelay,
        int maxAttempts,
        CancellationToken ct = default)
    {
        if (messageId == Guid.Empty)
        {
            throw new ArgumentException("Message ID cannot be Guid.Empty.", nameof(messageId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(workerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(errorMessage);
        ArgumentOutOfRangeException.ThrowIfLessThan(retryDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttempts);
        ct.ThrowIfCancellationRequested();

        var row = await _db.OutboxMessages
            .FirstOrDefaultAsync(x => x.Id == messageId, ct)
            .ConfigureAwait(false);

        if (row is null ||
            row.Status != (int)OutboxMessageStatus.Processing ||
            !string.Equals(row.LockedBy, workerId, StringComparison.Ordinal))
        {
            return;
        }

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        row.AttemptCount++;
        row.LastError = errorMessage.Length > MaxErrorLength ? errorMessage[..MaxErrorLength] : errorMessage;
        row.LockedBy = null;
        row.LockedUntilUtc = null;

        if (row.AttemptCount >= maxAttempts)
        {
            row.Status = (int)OutboxMessageStatus.Failed;
        }
        else
        {
            row.Status = (int)OutboxMessageStatus.Pending;
            row.NextRetryAtUtc = nowUtc.Add(retryDelay);
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> ReleaseExpiredLeasesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var nowUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var processingStatus = (int)OutboxMessageStatus.Processing;

        var expired = await _db.OutboxMessages
            .Where(x => x.Status == processingStatus && x.LockedUntilUtc != null && x.LockedUntilUtc <= nowUtc)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (expired.Count == 0)
        {
            return 0;
        }

        foreach (var row in expired)
        {
            row.Status = (int)OutboxMessageStatus.Pending;
            row.LockedBy = null;
            row.LockedUntilUtc = null;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return expired.Count;
    }

    private static (string? TraceParent, string? TraceState) CaptureCurrentTraceContext()
    {
        var current = Activity.Current;
        if (current is null)
        {
            return (null, null);
        }

        var bag = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        GeoAssetsActivitySource.InjectTraceContext(
            current,
            bag,
            static (dict, key, val) => dict[key] = val);

        bag.TryGetValue("traceparent", out var traceParent);
        bag.TryGetValue("tracestate", out var traceState);
        return (traceParent, traceState);
    }

    private static IntegrationEnvelope MapToEnvelope(OutboxMessageRecord row)
    {
        IReadOnlyDictionary<string, string>? headers = null;
        if (!string.IsNullOrWhiteSpace(row.HeadersJson))
        {
            headers = JsonSerializer.Deserialize<Dictionary<string, string>>(row.HeadersJson);
        }

        var occurredAtUtc = new DateTimeOffset(DateTime.SpecifyKind(row.CreatedAtUtc, DateTimeKind.Utc));

        return new IntegrationEnvelope(
            MessageId: row.Id,
            MessageType: row.MessageType,
            SchemaVersion: row.SchemaVersion,
            PartitionKey: row.PartitionKey,
            ContentType: row.ContentType,
            Payload: row.Payload,
            OccurredAtUtc: occurredAtUtc,
            CatalogInstanceId: row.CatalogInstanceId,
            SourceService: row.SourceService,
            CorrelationId: row.CorrelationId,
            CausationId: row.CausationId,
            TraceParent: row.TraceParent,
            TraceState: row.TraceState,
            Headers: headers);
    }
}
