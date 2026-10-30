using System.Diagnostics;
using System.Globalization;
using System.Text;
using Confluent.Kafka;
using GeoAssets.Core.Messaging;
using GeoAssets.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Workflow.Messaging.Kafka;

/// <summary>
/// Background Apache Kafka consumer enforcing catalog shard fencing, wire content-type
/// verification, idempotent inbox deduplication, and W3C distributed trace continuation.
/// </summary>
public sealed partial class KafkaIntegrationEventConsumer<TEvent> : BackgroundService
    where TEvent : notnull
{
    private static readonly HashSet<string> ReservedHeaderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "messageId",
        "messageType",
        "schemaVersion",
        "contentType",
        "catalogInstanceId",
        "correlationId",
        "causationId",
        "sourceService",
        "traceparent",
        "tracestate",
    };

    private static readonly Action<ILogger, Guid, string?, string, Exception?> LogShardFencedDelegate =
        LoggerMessage.Define<Guid, string?, string>(
            LogLevel.Warning,
            new EventId(1, "KafkaCatalogShardFenced"),
            "Skipping Kafka message {MessageId}: envelope catalog '{EnvelopeCatalog}' does not match target catalog '{TargetCatalog}'.");

    private static readonly Action<ILogger, string, string, Exception?> LogConsumeLoopErrorDelegate =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(2, "KafkaConsumeLoopError"),
            "Error processing Kafka message on topic '{Topic}' for consumer '{ConsumerName}'.");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIntegrationMessageSerializer _serializer;
    private readonly TimeProvider _timeProvider;
    private readonly KafkaConsumerOptions _options;
    private readonly GeoAssetsActivitySource _tracer;
    private readonly ILogger<KafkaIntegrationEventConsumer<TEvent>> _logger;

    public KafkaIntegrationEventConsumer(
        IServiceScopeFactory scopeFactory,
        IIntegrationMessageSerializer serializer,
        TimeProvider timeProvider,
        IOptions<KafkaConsumerOptions> options,
        GeoAssetsActivitySource tracer,
        ILogger<KafkaIntegrationEventConsumer<TEvent>> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tracer);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _serializer = serializer;
        _timeProvider = timeProvider;
        _options = options.Value ?? throw new InvalidOperationException("KafkaConsumerOptions value cannot be null.");
        _tracer = tracer;
        _logger = logger;
    }

    public async Task<bool> ProcessEnvelopeAsync(IntegrationEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ct.ThrowIfCancellationRequested();

        var parentContext = ExtractRemoteContext(envelope);
        var destination = string.IsNullOrWhiteSpace(_options.TopicName) ? "kafka" : _options.TopicName;

        using var activity = _tracer.StartIntegrationConsumeActivity(
            envelope.MessageType,
            envelope.MessageId,
            "kafka",
            destination,
            _options.GroupId,
            envelope.CatalogInstanceId,
            parentContext);

        if (!string.IsNullOrWhiteSpace(_options.TargetCatalogInstanceId) &&
            !string.Equals(envelope.CatalogInstanceId, _options.TargetCatalogInstanceId, StringComparison.Ordinal))
        {
            activity?.SetTag("geoassets.catalog.fenced", true);
            LogShardFencedDelegate(_logger, envelope.MessageId, envelope.CatalogInstanceId, _options.TargetCatalogInstanceId, null);
            return false;
        }

        try
        {
            if (!string.Equals(envelope.ContentType, _serializer.ContentType, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException(
                    $"Unsupported integration envelope content type '{envelope.ContentType}'. Expected '{_serializer.ContentType}'.");
            }

            return await DispatchWithinScopeAsync(envelope, destination, activity, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            GeoAssetsActivitySource.RecordException(activity, ex);
            throw;
        }
    }

    internal static IntegrationEnvelope ExtractEnvelope(
        ConsumeResult<string?, byte[]> consumeResult,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(consumeResult);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (consumeResult.Message is null)
        {
            throw new FormatException("Kafka ConsumeResult does not contain a message.");
        }

        var rawHeaders = consumeResult.Message.Headers;
        var rawMessageId = ReadHeaderString(rawHeaders, "messageId");
        if (string.IsNullOrWhiteSpace(rawMessageId) ||
            !Guid.TryParse(rawMessageId, out var messageId) ||
            messageId == Guid.Empty)
        {
            throw new FormatException("Kafka message is missing a valid non-empty 'messageId' header.");
        }

        var messageType = ReadHeaderString(rawHeaders, "messageType");
        if (string.IsNullOrWhiteSpace(messageType))
        {
            messageType = "unknown";
        }

        var partitionKey = !string.IsNullOrWhiteSpace(consumeResult.Message.Key)
            ? consumeResult.Message.Key
            : messageId.ToString("D");

        var occurredAtUtc = consumeResult.Message.Timestamp.Type != TimestampType.NotAvailable
            ? new DateTimeOffset(DateTime.SpecifyKind(consumeResult.Message.Timestamp.UtcDateTime, DateTimeKind.Utc))
            : timeProvider.GetUtcNow();

        return new IntegrationEnvelope(
            MessageId: messageId,
            MessageType: messageType,
            SchemaVersion: ParseSchemaVersion(rawHeaders),
            PartitionKey: partitionKey,
            ContentType: ReadHeaderString(rawHeaders, "contentType") ?? string.Empty,
            Payload: consumeResult.Message.Value ?? [],
            OccurredAtUtc: occurredAtUtc,
            CatalogInstanceId: ReadHeaderString(rawHeaders, "catalogInstanceId"),
            SourceService: ReadHeaderString(rawHeaders, "sourceService"),
            CorrelationId: ReadHeaderString(rawHeaders, "correlationId"),
            CausationId: ReadHeaderString(rawHeaders, "causationId"),
            TraceParent: ReadHeaderString(rawHeaders, "traceparent"),
            TraceState: ReadHeaderString(rawHeaders, "tracestate"),
            Headers: ExtractCustomHeaders(rawHeaders));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.IsNullOrWhiteSpace(_options.TopicName))
        {
            throw new InvalidOperationException("KafkaConsumerOptions.TopicName must be configured.");
        }

        using var consumer = new ConsumerBuilder<string?, byte[]>(BuildConsumerConfig()).Build();
        consumer.Subscribe(_options.TopicName);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ConsumeNextAsync(consumer, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogConsumeLoopErrorDelegate(_logger, _options.TopicName, _options.ConsumerName, ex);
                    try
                    {
                        await Task.Delay(_options.IdlePollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        break;
                    }
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private async Task<bool> DispatchWithinScopeAsync(
        IntegrationEnvelope envelope,
        string destination,
        Activity? activity,
        CancellationToken ct)
    {
        var scope = _scopeFactory.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var inbox = scope.ServiceProvider.GetRequiredService<IIdempotentInbox>();
            if (await inbox.HasProcessedAsync(envelope.MessageId, _options.ConsumerName, ct).ConfigureAwait(false))
            {
                activity?.SetTag("messaging.idempotent.duplicate", true);
                return false;
            }

            var evt = _serializer.Deserialize<TEvent>(envelope.Payload);
            var context = CreateMessageContext(envelope, destination);

            var handler = scope.ServiceProvider.GetRequiredService<IIntegrationEventHandler<TEvent>>();
            await handler.HandleAsync(evt, context, ct).ConfigureAwait(false);

            var recorded = await inbox.TryRecordProcessedAsync(
                envelope.MessageId,
                _options.ConsumerName,
                envelope.MessageType,
                _options.InboxRetentionPeriod,
                ct).ConfigureAwait(false);

            if (!recorded)
            {
                activity?.SetTag("messaging.idempotent.duplicate", true);
                return false;
            }

            return true;
        }
    }

    private IntegrationMessageContext CreateMessageContext(IntegrationEnvelope envelope, string destination) =>
        new(
            MessageId: envelope.MessageId,
            MessageType: envelope.MessageType,
            SchemaVersion: envelope.SchemaVersion,
            PartitionKey: envelope.PartitionKey,
            ContentType: envelope.ContentType,
            TransportName: "kafka",
            DestinationName: destination,
            ConsumerName: _options.ConsumerName,
            OccurredAtUtc: envelope.OccurredAtUtc,
            ReceivedAtUtc: _timeProvider.GetUtcNow(),
            CatalogInstanceId: envelope.CatalogInstanceId,
            CorrelationId: envelope.CorrelationId,
            CausationId: envelope.CausationId,
            TraceParent: envelope.TraceParent,
            TraceState: envelope.TraceState,
            Headers: envelope.Headers);

    private ConsumerConfig BuildConsumerConfig()
    {
        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            GroupId = _options.GroupId,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
        };

        foreach (var (key, value) in _options.AdditionalConfig)
        {
            config.Set(key, value);
        }

        return config;
    }

    private async Task ConsumeNextAsync(IConsumer<string?, byte[]> consumer, CancellationToken stoppingToken)
    {
        var result = consumer.Consume(TimeSpan.Zero);
        if (result is null || result.IsPartitionEOF)
        {
            await Task.Delay(_options.IdlePollInterval, _timeProvider, stoppingToken).ConfigureAwait(false);
            return;
        }

        var envelope = ExtractEnvelope(result, _timeProvider);
        await ProcessEnvelopeAsync(envelope, stoppingToken).ConfigureAwait(false);
        consumer.Commit(result);
    }

    private static int ParseSchemaVersion(Headers? rawHeaders)
    {
        var rawSchemaVersion = ReadHeaderString(rawHeaders, "schemaVersion");
        if (string.IsNullOrWhiteSpace(rawSchemaVersion))
        {
            return 1;
        }

        if (!int.TryParse(rawSchemaVersion, NumberStyles.Integer, CultureInfo.InvariantCulture, out var schemaVersion) ||
            schemaVersion <= 0)
        {
            throw new FormatException($"Kafka message has an invalid 'schemaVersion' header '{rawSchemaVersion}'.");
        }

        return schemaVersion;
    }

    private static Dictionary<string, string>? ExtractCustomHeaders(Headers? rawHeaders)
    {
        if (rawHeaders is null)
        {
            return null;
        }

        Dictionary<string, string>? customHeaders = null;
        foreach (var header in rawHeaders)
        {
            if (string.IsNullOrEmpty(header.Key) || ReservedHeaderNames.Contains(header.Key))
            {
                continue;
            }

            var bytes = header.GetValueBytes();
            if (bytes is null) continue;
            customHeaders ??= new Dictionary<string, string>(StringComparer.Ordinal);
            customHeaders[header.Key] = Encoding.UTF8.GetString(bytes);
        }

        return customHeaders;
    }

    private static string? ReadHeaderString(Headers? headers, string key)
    {
        if (headers is null) return null;

        string? result = null;
        foreach (var header in headers)
        {
            if (string.Equals(header.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                var bytes = header.GetValueBytes();
                result = bytes is null ? null : Encoding.UTF8.GetString(bytes);
            }
        }

        return result;
    }

    private static ActivityContext ExtractRemoteContext(IntegrationEnvelope envelope) =>
        GeoAssetsActivitySource.ExtractParentContext(
            envelope,
            static (carrier, fieldName, out fieldValue, out fieldValues) =>
            {
                fieldValues = null;
                if (carrier is not IntegrationEnvelope env)
                {
                    fieldValue = null;
                    return;
                }

                if (string.Equals(fieldName, "traceparent", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(env.TraceParent))
                {
                    fieldValue = env.TraceParent;
                    return;
                }

                if (string.Equals(fieldName, "tracestate", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(env.TraceState))
                {
                    fieldValue = env.TraceState;
                    return;
                }

                if (env.Headers is not null)
                {
                    foreach (var (key, value) in env.Headers)
                    {
                        if (string.Equals(key, fieldName, StringComparison.OrdinalIgnoreCase))
                        {
                            fieldValue = value;
                            return;
                        }
                    }
                }

                fieldValue = null;
            },
            isRemote: true);
}
