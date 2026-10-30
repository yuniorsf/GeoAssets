using System.Collections.Concurrent;
using System.Diagnostics;
using Azure.Messaging.ServiceBus;
using GeoAssets.Core.Messaging;
using GeoAssets.Infrastructure.Observability;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoAssets.Workflow.Messaging.ServiceBus;

/// <summary>
/// Publishes binary <see cref="IntegrationEnvelope"/> messages to Azure Service Bus topics
/// with W3C distributed trace propagation, chunked message batching, and optional per-catalog topic routing.
/// </summary>
public sealed partial class ServiceBusIntegrationEventPublisher : IIntegrationEventPublisher, IAsyncDisposable
{
    private const string CatalogPlaceholder = "{catalogInstanceId}";

    private readonly ServiceBusClient? _client;
    private readonly ServiceBusSender? _injectedSender;
    private readonly ConcurrentDictionary<string, ServiceBusSender> _sendersByTopic = new(StringComparer.Ordinal);
    private readonly ServiceBusPublisherOptions _options;
    private readonly GeoAssetsActivitySource _tracer;
    private readonly ILogger<ServiceBusIntegrationEventPublisher> _logger;
    private bool _disposed;

    public ServiceBusIntegrationEventPublisher(
        IOptions<ServiceBusPublisherOptions> options,
        GeoAssetsActivitySource tracer,
        ILogger<ServiceBusIntegrationEventPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tracer);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value ?? throw new InvalidOperationException("ServiceBusPublisherOptions value cannot be null.");
        _tracer = tracer;
        _logger = logger;
        _client = new ServiceBusClient(_options.ConnectionString);
    }

    internal ServiceBusIntegrationEventPublisher(
        ServiceBusSender sender,
        IOptions<ServiceBusPublisherOptions> options,
        GeoAssetsActivitySource tracer,
        ILogger<ServiceBusIntegrationEventPublisher> logger)
    {
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(tracer);
        ArgumentNullException.ThrowIfNull(logger);

        _injectedSender = sender;
        _options = options.Value ?? throw new InvalidOperationException("ServiceBusPublisherOptions value cannot be null.");
        _tracer = tracer;
        _logger = logger;
    }

    public async Task PublishAsync(IntegrationEnvelope envelope, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ct.ThrowIfCancellationRequested();

        var topic = ResolveTopicName(_options.TopicName, envelope.CatalogInstanceId);
        var sender = GetSender(topic);
        var parentContext = ResolveParentActivityContext(envelope);

        using var activity = _tracer.StartIntegrationPublishActivity(
            envelope.MessageType,
            envelope.MessageId,
            "servicebus",
            topic,
            envelope.CatalogInstanceId,
            parentContext);

        var message = BuildServiceBusMessage(envelope, activity);

        try
        {
            await sender.SendMessageAsync(message, ct).ConfigureAwait(false);
            LogPublished(_logger, envelope.MessageId, envelope.MessageType, topic);
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

        if (envelopes.Count == 0)
        {
            return;
        }

        var groupedByTopic = new Dictionary<string, List<IntegrationEnvelope>>(StringComparer.Ordinal);
        var topicOrder = new List<string>();
        foreach (var envelope in envelopes)
        {
            ArgumentNullException.ThrowIfNull(envelope);
            var topic = ResolveTopicName(_options.TopicName, envelope.CatalogInstanceId);
            if (!groupedByTopic.TryGetValue(topic, out var list))
            {
                list = [];
                groupedByTopic.Add(topic, list);
                topicOrder.Add(topic);
            }

            list.Add(envelope);
        }

        foreach (var topic in topicOrder)
        {
            ct.ThrowIfCancellationRequested();
            await SendTopicBatchAsync(topic, groupedByTopic[topic], ct).ConfigureAwait(false);
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

    internal static ServiceBusMessage BuildServiceBusMessage(IntegrationEnvelope envelope, Activity? activity)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var message = new ServiceBusMessage(BinaryData.FromBytes(envelope.Payload))
        {
            ContentType = envelope.ContentType,
            MessageId = envelope.MessageId.ToString("D"),
            Subject = envelope.MessageType,
            PartitionKey = envelope.PartitionKey,
        };

        if (envelope.CorrelationId is not null)
        {
            message.CorrelationId = envelope.CorrelationId;
        }

        if (envelope.Headers is not null)
        {
            foreach (var (key, value) in envelope.Headers)
            {
                message.ApplicationProperties[key] = value;
            }
        }

        message.ApplicationProperties["schemaVersion"] = envelope.SchemaVersion;

        if (envelope.CatalogInstanceId is not null)
        {
            message.ApplicationProperties["catalogInstanceId"] = envelope.CatalogInstanceId;
        }

        if (envelope.CausationId is not null)
        {
            message.ApplicationProperties["causationId"] = envelope.CausationId;
        }

        if (envelope.SourceService is not null)
        {
            message.ApplicationProperties["sourceService"] = envelope.SourceService;
        }

        ApplyTraceProperties(message.ApplicationProperties, envelope, activity);
        return message;
    }

    private static void ApplyTraceProperties(
        IDictionary<string, object> properties,
        IntegrationEnvelope envelope,
        Activity? activity)
    {
        var traceInjected = false;
        if (activity is not null)
        {
            GeoAssetsActivitySource.InjectTraceContext(
                activity,
                properties,
                (props, key, val) =>
                {
                    props[key] = val;
                    if (string.Equals(key, "traceparent", StringComparison.OrdinalIgnoreCase))
                    {
                        traceInjected = true;
                    }
                });
        }

        if (!traceInjected && !string.IsNullOrWhiteSpace(envelope.TraceParent))
        {
            properties["traceparent"] = envelope.TraceParent;
            if (!string.IsNullOrWhiteSpace(envelope.TraceState))
            {
                properties["tracestate"] = envelope.TraceState;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_injectedSender is not null)
        {
            await _injectedSender.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var sender in _sendersByTopic.Values)
        {
            await sender.DisposeAsync().ConfigureAwait(false);
        }

        _sendersByTopic.Clear();

        if (_client is not null)
        {
            await _client.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task SendTopicBatchAsync(
        string topic,
        List<IntegrationEnvelope> topicEnvelopes,
        CancellationToken ct)
    {
        var sender = GetSender(topic);
        var activities = new List<Activity?>(topicEnvelopes.Count);
        var messages = new List<ServiceBusMessage>(topicEnvelopes.Count);

        try
        {
            foreach (var env in topicEnvelopes)
            {
                var parentCtx = ResolveParentActivityContext(env);
                var activity = _tracer.StartIntegrationPublishActivity(
                    env.MessageType,
                    env.MessageId,
                    "servicebus",
                    topic,
                    env.CatalogInstanceId,
                    parentCtx);
                activities.Add(activity);
                messages.Add(BuildServiceBusMessage(env, activity));
            }

            await SendChunkedBatchesAsync(sender, messages, ct).ConfigureAwait(false);
            LogBatchPublished(_logger, messages.Count, topic);
        }
        catch (Exception ex)
        {
            foreach (var activity in activities)
            {
                GeoAssetsActivitySource.RecordException(activity, ex);
            }

            LogBatchPublishFailed(_logger, messages.Count, topic, ex);
            throw;
        }
        finally
        {
            foreach (var activity in activities)
            {
                activity?.Dispose();
            }
        }
    }

    private static async Task SendChunkedBatchesAsync(
        ServiceBusSender sender,
        List<ServiceBusMessage> messages,
        CancellationToken ct)
    {
        var batch = await sender.CreateMessageBatchAsync(ct).ConfigureAwait(false);
        if (batch is null)
        {
            await sender.SendMessagesAsync(messages, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            foreach (var msg in messages)
            {
                if (batch.TryAddMessage(msg))
                {
                    continue;
                }

                if (batch.Count > 0)
                {
                    await sender.SendMessagesAsync(batch, ct).ConfigureAwait(false);
                    batch.Dispose();
                    batch = await sender.CreateMessageBatchAsync(ct).ConfigureAwait(false)
                        ?? throw new InvalidOperationException("ServiceBusSender.CreateMessageBatchAsync returned null while chunking batch.");
                }

                if (!batch.TryAddMessage(msg))
                {
                    throw new InvalidOperationException(
                        $"Service Bus message '{msg.MessageId}' exceeds the maximum ServiceBusMessageBatch size.");
                }
            }

            if (batch.Count > 0)
            {
                await sender.SendMessagesAsync(batch, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            batch.Dispose();
        }
    }

    private ServiceBusSender GetSender(string resolvedTopic)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_injectedSender is not null)
        {
            return _injectedSender;
        }

        var client = _client;
        ObjectDisposedException.ThrowIf(client is null, this);

        return _sendersByTopic.GetOrAdd(
            resolvedTopic,
            static (topic, sbClient) => sbClient.CreateSender(topic),
            client);
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
        Message = "Published integration message {MessageId} ({MessageType}) to Service Bus topic '{Topic}'.")]
    private static partial void LogPublished(
        ILogger logger,
        Guid messageId,
        string messageType,
        string topic);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Published batch of {Count} integration messages to Service Bus topic '{Topic}'.")]
    private static partial void LogBatchPublished(
        ILogger logger,
        int count,
        string topic);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to publish integration message {MessageId} ({MessageType}) to Service Bus topic '{Topic}'.")]
    private static partial void LogPublishFailed(
        ILogger logger,
        Guid messageId,
        string messageType,
        string topic,
        Exception ex);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Failed to publish batch of {Count} integration messages to Service Bus topic '{Topic}'.")]
    private static partial void LogBatchPublishFailed(
        ILogger logger,
        int count,
        string topic,
        Exception ex);
}
