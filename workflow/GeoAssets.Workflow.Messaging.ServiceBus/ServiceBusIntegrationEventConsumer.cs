using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Azure.Messaging.ServiceBus;
using GeoAssets.Core.Messaging;
using GeoAssets.Infrastructure.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Workflow.Messaging.ServiceBus;

/// <summary>
/// Azure Service Bus subscription consumer enforcing catalog shard fencing, wire content-type
/// verification, idempotent inbox deduplication, and explicit poison/transient dispositions.
/// </summary>
public sealed partial class ServiceBusIntegrationEventConsumer<TEvent> : IHostedService, IAsyncDisposable
    where TEvent : notnull
{
    private static readonly HashSet<string> ReservedPropertyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "schemaVersion",
        "catalogInstanceId",
        "causationId",
        "sourceService",
        "traceparent",
        "tracestate",
    };

    private static readonly Action<ILogger, Guid, string?, string, Exception?> LogShardFencedDelegate =
        LoggerMessage.Define<Guid, string?, string>(
            LogLevel.Warning,
            new EventId(1, "ServiceBusCatalogShardFenced"),
            "Fencing Service Bus message {MessageId}: envelope catalog '{EnvelopeCatalog}' does not match target catalog '{TargetCatalog}'.");

    private static readonly Action<ILogger, string, string, Exception?> LogPoisonMessageDelegate =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(2, "ServiceBusPoisonMessage"),
            "Dead-lettering poison Service Bus message '{MessageId}': {Reason}");

    private static readonly Action<ILogger, Guid, string, Exception?> LogTransientFailureDelegate =
        LoggerMessage.Define<Guid, string>(
            LogLevel.Warning,
            new EventId(3, "ServiceBusTransientFailure"),
            "Abandoning Service Bus message {MessageId} ({MessageType}) for retry after transient handler error.");

    private static readonly Action<ILogger, string, string, Exception?> LogProcessorErrorDelegate =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(4, "ServiceBusProcessorError"),
            "Azure Service Bus processor error on entity '{EntityPath}' (source: {ErrorSource}).");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IIntegrationMessageSerializer _serializer;
    private readonly TimeProvider _timeProvider;
    private readonly ServiceBusConsumerOptions _options;
    private readonly GeoAssetsActivitySource _tracer;
    private readonly ILogger<ServiceBusIntegrationEventConsumer<TEvent>> _logger;

    private ServiceBusClient? _client;
    private ServiceBusProcessor? _processor;

    public ServiceBusIntegrationEventConsumer(
        IServiceScopeFactory scopeFactory,
        IIntegrationMessageSerializer serializer,
        TimeProvider timeProvider,
        IOptions<ServiceBusConsumerOptions> options,
        GeoAssetsActivitySource tracer,
        ILogger<ServiceBusIntegrationEventConsumer<TEvent>> logger)
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
        _options = options.Value ?? throw new InvalidOperationException("ServiceBusConsumerOptions value cannot be null.");
        _tracer = tracer;
        _logger = logger;
    }

    public Task<ServiceBusConsumeDisposition> ProcessReceivedMessageAsync(
        ServiceBusReceivedMessage receivedMessage,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(receivedMessage);
        ct.ThrowIfCancellationRequested();

        return TryExtractEnvelope(receivedMessage, out var envelope)
            ? ProcessEnvelopeAsync(envelope, ct)
            : Task.FromResult(ServiceBusConsumeDisposition.DeadLetterPoison);
    }

    public async Task<ServiceBusConsumeDisposition> ProcessEnvelopeAsync(
        IntegrationEnvelope envelope,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ct.ThrowIfCancellationRequested();

        var parentContext = ExtractRemoteContext(envelope);
        var destination = string.IsNullOrWhiteSpace(_options.TopicName) ? "servicebus" : _options.TopicName;
        var consumerGroup = string.IsNullOrWhiteSpace(_options.SubscriptionName)
            ? _options.ConsumerName
            : _options.SubscriptionName;

        using var activity = _tracer.StartIntegrationConsumeActivity(
            envelope.MessageType,
            envelope.MessageId,
            "servicebus",
            destination,
            consumerGroup,
            envelope.CatalogInstanceId,
            parentContext);

        if (!string.IsNullOrWhiteSpace(_options.TargetCatalogInstanceId) &&
            !string.Equals(envelope.CatalogInstanceId, _options.TargetCatalogInstanceId, StringComparison.Ordinal))
        {
            activity?.SetTag("geoassets.catalog.fenced", true);
            LogShardFencedDelegate(_logger, envelope.MessageId, envelope.CatalogInstanceId, _options.TargetCatalogInstanceId, null);
            return _options.DeadLetterOnCatalogMismatch
                ? ServiceBusConsumeDisposition.DeadLetterPoison
                : ServiceBusConsumeDisposition.CatalogShardFenced;
        }

        if (!string.Equals(envelope.ContentType, _serializer.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            var contentEx = new FormatException(
                $"Unsupported integration envelope content type '{envelope.ContentType}'. Expected '{_serializer.ContentType}'.");
            GeoAssetsActivitySource.RecordException(activity, contentEx);
            LogPoisonMessageDelegate(_logger, envelope.MessageId.ToString("D"), contentEx.Message, contentEx);
            return ServiceBusConsumeDisposition.DeadLetterPoison;
        }

        try
        {
            return await DispatchScopedEnvelopeAsync(envelope, destination, activity, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            GeoAssetsActivitySource.RecordException(activity, ex);
            LogTransientFailureDelegate(_logger, envelope.MessageId, envelope.MessageType, ex);
            return ServiceBusConsumeDisposition.AbandonTransient;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.ConnectionString))
        {
            throw new InvalidOperationException("ServiceBusConsumerOptions.ConnectionString must be configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.TopicName))
        {
            throw new InvalidOperationException("ServiceBusConsumerOptions.TopicName must be configured.");
        }

        if (string.IsNullOrWhiteSpace(_options.SubscriptionName))
        {
            throw new InvalidOperationException("ServiceBusConsumerOptions.SubscriptionName must be configured.");
        }

        _client = new ServiceBusClient(_options.ConnectionString);
        _processor = _client.CreateProcessor(
            _options.TopicName,
            _options.SubscriptionName,
            new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false,
                MaxConcurrentCalls = Math.Max(1, _options.MaxConcurrentCalls),
            });

        _processor.ProcessMessageAsync += OnProcessMessageAsync;
        _processor.ProcessErrorAsync += OnProcessErrorAsync;

        return _processor.StartProcessingAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null && _processor.IsProcessing)
        {
            return _processor.StopProcessingAsync(cancellationToken);
        }

        return Task.CompletedTask;
    }

    private bool TryExtractEnvelope(
        ServiceBusReceivedMessage receivedMessage,
        [NotNullWhen(true)] out IntegrationEnvelope? envelope)
    {
        if (!Guid.TryParse(receivedMessage.MessageId, out var messageId) || messageId == Guid.Empty)
        {
            LogPoisonMessageDelegate(
                _logger,
                receivedMessage.MessageId ?? "<null>",
                "MessageId is missing or is not a valid non-empty Guid.",
                null);
            envelope = null;
            return false;
        }

        if (!TryExtractSchemaVersion(receivedMessage.ApplicationProperties, out var schemaVersion))
        {
            LogPoisonMessageDelegate(
                _logger,
                messageId.ToString("D"),
                "ApplicationProperties['schemaVersion'] is invalid.",
                null);
            envelope = null;
            return false;
        }

        var messageType = !string.IsNullOrWhiteSpace(receivedMessage.Subject)
            ? receivedMessage.Subject
            : "unknown";

        var partitionKey = !string.IsNullOrWhiteSpace(receivedMessage.PartitionKey)
            ? receivedMessage.PartitionKey
            : messageId.ToString("D");

        var occurredAtUtc = receivedMessage.EnqueuedTime != default
            ? receivedMessage.EnqueuedTime
            : _timeProvider.GetUtcNow();

        envelope = new IntegrationEnvelope(
            MessageId: messageId,
            MessageType: messageType,
            SchemaVersion: schemaVersion,
            PartitionKey: partitionKey,
            ContentType: receivedMessage.ContentType ?? string.Empty,
            Payload: receivedMessage.Body.ToMemory(),
            OccurredAtUtc: occurredAtUtc,
            CatalogInstanceId: ReadOptionalString(receivedMessage.ApplicationProperties, "catalogInstanceId"),
            SourceService: ReadOptionalString(receivedMessage.ApplicationProperties, "sourceService"),
            CorrelationId: receivedMessage.CorrelationId,
            CausationId: ReadOptionalString(receivedMessage.ApplicationProperties, "causationId"),
            TraceParent: ReadOptionalString(receivedMessage.ApplicationProperties, "traceparent"),
            TraceState: ReadOptionalString(receivedMessage.ApplicationProperties, "tracestate"),
            Headers: ExtractCustomProperties(receivedMessage.ApplicationProperties));
        return true;
    }

    private async Task<ServiceBusConsumeDisposition> DispatchScopedEnvelopeAsync(
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
                return ServiceBusConsumeDisposition.DuplicateSkipped;
            }

            TEvent evt;
            try
            {
                evt = _serializer.Deserialize<TEvent>(envelope.Payload);
            }
            catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException)
            {
                GeoAssetsActivitySource.RecordException(activity, ex);
                LogPoisonMessageDelegate(_logger, envelope.MessageId.ToString("D"), ex.Message, ex);
                return ServiceBusConsumeDisposition.DeadLetterPoison;
            }

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
                return ServiceBusConsumeDisposition.DuplicateSkipped;
            }

            return ServiceBusConsumeDisposition.Complete;
        }
    }

    private IntegrationMessageContext CreateMessageContext(IntegrationEnvelope envelope, string destination) =>
        new(
            MessageId: envelope.MessageId,
            MessageType: envelope.MessageType,
            SchemaVersion: envelope.SchemaVersion,
            PartitionKey: envelope.PartitionKey,
            ContentType: envelope.ContentType,
            TransportName: "servicebus",
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

    private static Dictionary<string, string>? ExtractCustomProperties(IReadOnlyDictionary<string, object> properties)
    {
        Dictionary<string, string>? customHeaders = null;
        foreach (var (key, value) in properties)
        {
            if (string.IsNullOrEmpty(key) || ReservedPropertyNames.Contains(key) || value is null)
            {
                continue;
            }

            customHeaders ??= new Dictionary<string, string>(StringComparer.Ordinal);
            customHeaders[key] = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return customHeaders;
    }

    public async ValueTask DisposeAsync()
    {
        if (_processor is not null)
        {
            _processor.ProcessMessageAsync -= OnProcessMessageAsync;
            _processor.ProcessErrorAsync -= OnProcessErrorAsync;
            await _processor.DisposeAsync().ConfigureAwait(false);
            _processor = null;
        }

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
            _client = null;
        }
    }

    private async Task OnProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var disposition = await ProcessReceivedMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);

        switch (disposition)
        {
            case ServiceBusConsumeDisposition.Complete:
            case ServiceBusConsumeDisposition.DuplicateSkipped:
                await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
                break;

            case ServiceBusConsumeDisposition.AbandonTransient:
                await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken).ConfigureAwait(false);
                break;

            case ServiceBusConsumeDisposition.DeadLetterPoison:
                await args.DeadLetterMessageAsync(
                    args.Message,
                    deadLetterReason: "PoisonIntegrationMessage",
                    deadLetterErrorDescription: "Integration message failed envelope validation, catalog shard policy, or payload deserialization.",
                    cancellationToken: args.CancellationToken).ConfigureAwait(false);
                break;

            case ServiceBusConsumeDisposition.CatalogShardFenced:
                await args.CompleteMessageAsync(args.Message, args.CancellationToken).ConfigureAwait(false);
                break;
        }
    }

    private Task OnProcessErrorAsync(ProcessErrorEventArgs args)
    {
        LogProcessorErrorDelegate(_logger, args.EntityPath, args.ErrorSource.ToString(), args.Exception);
        return Task.CompletedTask;
    }

    private static bool TryExtractSchemaVersion(
        IReadOnlyDictionary<string, object> properties,
        out int schemaVersion)
    {
        foreach (var (key, rawValue) in properties)
        {
            if (!string.Equals(key, "schemaVersion", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            switch (rawValue)
            {
                case int intVal when intVal > 0:
                    schemaVersion = intVal;
                    return true;
                case long longVal when longVal is > 0 and <= int.MaxValue:
                    schemaVersion = (int)longVal;
                    return true;
                case string strVal when int.TryParse(strVal, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0:
                    schemaVersion = parsed;
                    return true;
                default:
                    schemaVersion = 0;
                    return false;
            }
        }

        schemaVersion = 1;
        return true;
    }

    private static string? ReadOptionalString(IReadOnlyDictionary<string, object> properties, string key)
    {
        foreach (var (propKey, propVal) in properties)
        {
            if (string.Equals(propKey, key, StringComparison.OrdinalIgnoreCase))
            {
                return propVal is null
                    ? null
                    : Convert.ToString(propVal, CultureInfo.InvariantCulture);
            }
        }

        return null;
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
