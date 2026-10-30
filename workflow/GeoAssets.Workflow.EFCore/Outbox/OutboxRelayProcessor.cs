using System.Diagnostics;
using GeoAssets.Core.Messaging;
using GeoAssets.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Workflow.Persistence.Outbox;

/// <summary>
/// Background relay worker that claims pending outbox batches under a bounded lease
/// and publishes them through <see cref="IIntegrationEventPublisher"/> with exponential backoff.
/// </summary>
public sealed partial class OutboxRelayProcessor : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIntegrationEventPublisher _publisher;
    private readonly TimeProvider _timeProvider;
    private readonly OutboxRelayOptions _options;
    private readonly GeoAssetsActivitySource _tracer;
    private readonly ILogger<OutboxRelayProcessor> _logger;

    public OutboxRelayProcessor(
        IServiceScopeFactory scopeFactory,
        IIntegrationEventPublisher publisher,
        TimeProvider timeProvider,
        IOptions<OutboxRelayOptions> options,
        GeoAssetsActivitySource tracer,
        ILogger<OutboxRelayProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(publisher);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tracer);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _timeProvider = timeProvider;
        _options = options.Value ?? throw new InvalidOperationException("OutboxRelayOptions value cannot be null.");
        _tracer = tracer;
        _logger = logger;
    }

    public static TimeSpan ComputeRetryDelay(int attemptCount, TimeSpan baseInterval, TimeSpan maxBackoff)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attemptCount, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(baseInterval, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBackoff, TimeSpan.Zero);

        var exponent = Math.Min(attemptCount, 6);
        var multiplier = 1L << exponent;
        var candidateTicks = baseInterval.Ticks * multiplier;
        if (candidateTicks <= 0 || candidateTicks >= maxBackoff.Ticks)
        {
            return maxBackoff;
        }

        return TimeSpan.FromTicks(candidateTicks);
    }

    public async Task<int> ProcessNextBatchAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var store = scope.ServiceProvider.GetRequiredService<IOutboxDispatcherStore>();
            var batch = await store
                .ClaimPendingBatchAsync(_options.BatchSize, _options.LeaseDuration, _options.WorkerId, ct)
                .ConfigureAwait(false);

            if (batch.Count == 0)
            {
                return 0;
            }

            using var batchActivity = _tracer.StartActivity("Outbox.RelayBatch");
            batchActivity?.SetTag("outbox.worker.id", _options.WorkerId);
            batchActivity?.SetTag("outbox.batch.claimed", batch.Count);

            var dispatched = 0;
            foreach (var pending in batch)
            {
                ct.ThrowIfCancellationRequested();
                if (await TryDispatchPendingMessageAsync(store, pending, ct).ConfigureAwait(false))
                {
                    dispatched++;
                }
            }

            batchActivity?.SetTag("outbox.batch.dispatched", dispatched);
            return dispatched;
        }
    }

    private async Task<bool> TryDispatchPendingMessageAsync(
        IOutboxDispatcherStore store,
        OutboxPendingMessage pending,
        CancellationToken ct)
    {
        using var itemActivity = StartRelayMessageActivity(pending.Envelope);

        try
        {
            await _publisher.PublishAsync(pending.Envelope, ct).ConfigureAwait(false);
            await store.MarkDispatchedAsync(pending.Envelope.MessageId, _options.WorkerId, ct).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            GeoAssetsActivitySource.RecordException(itemActivity, ex);

            var retryDelay = ComputeRetryDelay(
                pending.AttemptCount,
                _options.BasePollInterval,
                _options.MaxBackoffInterval);

            LogDispatchFailed(
                _logger,
                pending.Envelope.MessageId,
                pending.Envelope.MessageType,
                pending.AttemptCount + 1,
                retryDelay.TotalMilliseconds,
                ex);

            var failureReason = string.IsNullOrWhiteSpace(ex.Message)
                ? ex.GetType().Name
                : ex.Message;

            await store.MarkFailedAsync(
                pending.Envelope.MessageId,
                _options.WorkerId,
                failureReason,
                retryDelay,
                _options.MaxAttempts,
                ct).ConfigureAwait(false);

            return false;
        }
    }

    private Activity? StartRelayMessageActivity(IntegrationEnvelope envelope)
    {
        var parentContext = !string.IsNullOrWhiteSpace(envelope.TraceParent) &&
            ActivityContext.TryParse(envelope.TraceParent, envelope.TraceState, isRemote: false, out var parsedParent)
            ? parsedParent
            : default;

        var itemActivity = _tracer.StartActivity("Outbox.RelayMessage", ActivityKind.Internal, parentContext);
        if (itemActivity is not null)
        {
            itemActivity.SetTag("messaging.message.id", envelope.MessageId.ToString("D"));
            if (!string.IsNullOrWhiteSpace(envelope.TraceState))
            {
                itemActivity.TraceStateString = envelope.TraceState;
            }
        }

        return itemActivity;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableRelayWorker)
        {
            return;
        }

        var consecutiveLoopErrors = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var dispatched = await ProcessNextBatchAsync(stoppingToken).ConfigureAwait(false);
                consecutiveLoopErrors = 0;

                if (dispatched == 0)
                {
                    await Task.Delay(_options.BasePollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveLoopErrors++;
                var backoff = ComputeRetryDelay(
                    consecutiveLoopErrors,
                    _options.BasePollInterval,
                    _options.MaxBackoffInterval);

                LogRelayLoopError(_logger, _options.WorkerId, backoff.TotalMilliseconds, ex);

                try
                {
                    await Task.Delay(backoff, _timeProvider, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Outbox dispatch failed for message {MessageId} ({MessageType}) on attempt {Attempt}; retrying in {RetryDelayMs} ms.")]
    private static partial void LogDispatchFailed(
        ILogger logger,
        Guid messageId,
        string messageType,
        int attempt,
        double retryDelayMs,
        Exception ex);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Outbox relay worker '{WorkerId}' encountered an unexpected loop error; backing off for {BackoffMs} ms.")]
    private static partial void LogRelayLoopError(
        ILogger logger,
        string workerId,
        double backoffMs,
        Exception ex);
}
