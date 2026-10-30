using System.Diagnostics;
using System.Globalization;
using System.Text;
using Confluent.Kafka;
using GeoAssets.Core.Messaging;
using GeoAssets.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Workflow.Messaging.Kafka;

/// <summary>
/// Publishes binary <see cref="IntegrationEnvelope"/> payloads to Apache Kafka with W3C
/// distributed trace propagation and optional per-catalog topic routing.
/// </summary>
public sealed partial class KafkaIntegrationEventPublisher : IIntegrationEventPublisher, IDisposable
{
    private const string CatalogPlaceholder = "{catalogInstanceId}";

    private readonly IProducer<string?, byte[]> _producer;
    private readonly KafkaPublisherOptions _options;
    private readonly GeoAssetsActivitySource _tracer;
    private readonly ILogger<KafkaIntegrationEventPublisher> _logger;
    private bool _disposed;

    public KafkaIntegrationEventPublisher(
        IOptions<KafkaPublisherOptions> options,
        GeoAssetsActivitySource tracer,
        ILogger<KafkaIntegrationEventPublisher> logger)
        : this(CreateProducer(options), options, tracer, logger)
    {
    }

    internal KafkaIntegrationEventPublisher(
        IProducer<string?, byte[]> producer,
        IOptions<KafkaPublisherOptions> options,
        GeoAssetsActivitySource tracer,
        ILogger<KafkaIntegrationEventPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(producer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tracer);
        ArgumentNullException.ThrowIfNull(logger);

        _producer = producer;
        _options = options.Value ?? throw new InvalidOperationException("KafkaPublisherOptions value cannot be null.");
        _tracer = tracer;
        _logger = logger;
    }

    public async Task PublishAsync(IntegrationEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ct.ThrowIfCancellationRequested();

        var topic = ResolveTopicName(_options.TopicName, envelope.CatalogInstanceId);
        var parentContext = ResolveParentActivityContext(envelope);

        using var activity = _tracer.StartIntegrationPublishActivity(
            envelope.MessageType,
            envelope.MessageId,
            "kafka",
            topic,
            envelope.CatalogInstanceId,
            parentContext);

        var message = new Message<string?, byte[]>
        {
            Key = envelope.PartitionKey,
            Value = envelope.Payload.ToArray(),
            Timestamp = new Timestamp(envelope.OccurredAtUtc),
            Headers = BuildEnvelopeHeaders(envelope, activity),
        };

        try
        {
            var result = await _producer.ProduceAsync(topic, message, ct).ConfigureAwait(false);
            var partition = result?.Partition.Value ?? -1;
            var offset = result?.Offset.Value ?? -1L;
            LogPublished(_logger, envelope.MessageId, envelope.MessageType, topic, partition, offset);
        }
        catch (Exception ex)
        {
            GeoAssetsActivitySource.RecordException(activity, ex);
            LogPublishFailed(_logger, envelope.MessageId, envelope.MessageType, topic, ex);
            throw;
        }
    }

    public async Task PublishBatchAsync(IReadOnlyList<IntegrationEnvelope> envelopes, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelopes);
        ct.ThrowIfCancellationRequested();

        foreach (var envelope in envelopes)
        {
            ct.ThrowIfCancellationRequested();
            await PublishAsync(envelope, ct).ConfigureAwait(false);
        }
    }

    internal static string ResolveTopicName(string configuredTopic, string? catalogInstanceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredTopic);

        if (!configuredTopic.Contains(CatalogPlaceholder, StringComparison.Ordinal))
        {
            return configuredTopic;
        }

        if (string.IsNullOrWhiteSpace(catalogInstanceId) || !IsValidCatalogSlug(catalogInstanceId))
        {
            throw new ArgumentException(
                "A valid catalogInstanceId matching '^[a-zA-Z0-9._-]+$' is required when TopicName contains '{catalogInstanceId}'.",
                nameof(catalogInstanceId));
        }

        return configuredTopic.Replace(CatalogPlaceholder, catalogInstanceId, StringComparison.Ordinal);
    }

    internal static Headers BuildEnvelopeHeaders(IntegrationEnvelope envelope, Activity? activity)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var headers = new Headers();
        if (envelope.Headers is not null)
        {
            foreach (var (key, value) in envelope.Headers)
            {
                headers.Remove(key);
                headers.Add(key, Encoding.UTF8.GetBytes(value));
            }
        }

        SetHeader(headers, "messageId", envelope.MessageId.ToString("D"));
        SetHeader(headers, "messageType", envelope.MessageType);
        SetHeader(headers, "schemaVersion", envelope.SchemaVersion.ToString(CultureInfo.InvariantCulture));
        SetHeader(headers, "contentType", envelope.ContentType);

        if (envelope.CatalogInstanceId is not null)
        {
            SetHeader(headers, "catalogInstanceId", envelope.CatalogInstanceId);
        }

        if (envelope.CorrelationId is not null)
        {
            SetHeader(headers, "correlationId", envelope.CorrelationId);
        }

        if (envelope.CausationId is not null)
        {
            SetHeader(headers, "causationId", envelope.CausationId);
        }

        if (envelope.SourceService is not null)
        {
            SetHeader(headers, "sourceService", envelope.SourceService);
        }

        InjectEnvelopeTraceHeaders(headers, envelope, activity);
        return headers;
    }

    private static void InjectEnvelopeTraceHeaders(
        Headers headers,
        IntegrationEnvelope envelope,
        Activity? activity)
    {
        var traceInjected = false;
        if (activity is not null)
        {
            GeoAssetsActivitySource.InjectTraceContext(
                activity,
                headers,
                (h, key, val) =>
                {
                    SetHeader(h, key, val);
                    if (string.Equals(key, "traceparent", StringComparison.OrdinalIgnoreCase))
                    {
                        traceInjected = true;
                    }
                });
        }

        if (!traceInjected && !string.IsNullOrWhiteSpace(envelope.TraceParent))
        {
            SetHeader(headers, "traceparent", envelope.TraceParent);
            if (!string.IsNullOrWhiteSpace(envelope.TraceState))
            {
                SetHeader(headers, "tracestate", envelope.TraceState);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }

    private static IProducer<string?, byte[]> CreateProducer(IOptions<KafkaPublisherOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opts = options.Value ?? throw new InvalidOperationException("KafkaPublisherOptions value cannot be null.");

        var config = new ProducerConfig
        {
            BootstrapServers = opts.BootstrapServers,
            ClientId = opts.ClientId,
        };

        foreach (var (key, value) in opts.AdditionalConfig)
        {
            config.Set(key, value);
        }

        return new ProducerBuilder<string?, byte[]>(config).Build();
    }

    private static ActivityContext ResolveParentActivityContext(IntegrationEnvelope envelope)
    {
        if (Activity.Current is { } current)
        {
            return current.Context;
        }

        if (!string.IsNullOrWhiteSpace(envelope.TraceParent) &&
            ActivityContext.TryParse(envelope.TraceParent, envelope.TraceState, isRemote: false, out var storedCtx))
        {
            return storedCtx;
        }

        return default;
    }

    private static void SetHeader(Headers headers, string key, string value)
    {
        headers.Remove(key);
        headers.Add(key, Encoding.UTF8.GetBytes(value));
    }

    private static bool IsValidCatalogSlug(string value)
    {
        foreach (var ch in value)
        {
            if (!char.IsAsciiLetterOrDigit(ch) && ch is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return value.Length > 0;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Published integration message {MessageId} ({MessageType}) to Kafka topic '{Topic}' partition {Partition} offset {Offset}.")]
    private static partial void LogPublished(
        ILogger logger,
        Guid messageId,
        string messageType,
        string topic,
        int partition,
        long offset);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to publish integration message {MessageId} ({MessageType}) to Kafka topic '{Topic}'.")]
    private static partial void LogPublishFailed(
        ILogger logger,
        Guid messageId,
        string messageType,
        string topic,
        Exception ex);
}
