using System.Diagnostics;
using System.Reflection;
using System.Text;
using Confluent.Kafka;
using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Messaging.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Messaging.Kafka.Tests;

public sealed class KafkaIntegrationMessagingTests
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SampleOrgId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly ActivitySource TestSource = new("GeoAssets.Workflow.Messaging.Kafka.Tests.Integration");

    static KafkaIntegrationMessagingTests()
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        });
    }

    [Fact]
    public async Task PublishAsync_BinaryProtobufEnvelope_ProducesBytePayloadPartitionKeyAndCatalogHeader()
    {
        using var producer = new RecordingKafkaByteProducer();
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var serializer = new ProtobufIntegrationMessageSerializer();
        using var publisher = CreatePublisher(producer, tracer, topicTemplate: "geoassets.catalog.events");

        var domainEvent = CreateMutatedEvent("electric-coelba", "pole-ba-100");
        var payload = serializer.Serialize(domainEvent);
        var envelope = new IntegrationEnvelope(
            MessageId: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001"),
            MessageType: IntegrationEventTypes.FeatureCatalogMutatedV1,
            SchemaVersion: IntegrationEventTypes.CurrentSchemaVersion,
            PartitionKey: "electric-coelba:pole-ba-100",
            ContentType: serializer.ContentType,
            Payload: payload,
            OccurredAtUtc: FixedUtc,
            CatalogInstanceId: "electric-coelba",
            SourceService: "geoassets-server",
            CorrelationId: "corr-kafka-1");

        await publisher.PublishAsync(envelope);

        var produced = producer.Produced.Should().ContainSingle().Subject;
        produced.Topic.Should().Be("geoassets.catalog.events");
        produced.Message.Key.Should().Be("electric-coelba:pole-ba-100");
        produced.Message.Value.Should().Equal(payload);

        var decoded = serializer.Deserialize<FeatureCatalogMutatedEvent>(produced.Message.Value);
        decoded.CatalogInstanceId.Should().Be("electric-coelba");
        decoded.FeatureId.Should().Be("pole-ba-100");

        ReadHeader(produced.Message.Headers, "catalogInstanceId").Should().Be("electric-coelba");
    }

    [Fact]
    public async Task PublishBatchAsync_ProducesAllEnvelopesInSequence()
    {
        using var producer = new RecordingKafkaByteProducer();
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var serializer = new ProtobufIntegrationMessageSerializer();
        using var publisher = CreatePublisher(producer, tracer, topicTemplate: "geoassets.catalog.events");

        var env1 = CreateEnvelope(serializer, "electric-coelba", "feat-1", Guid.Parse("aaaaaaaa-0000-0000-0000-000000000011"));
        var env2 = CreateEnvelope(serializer, "sanitation-aguas", "feat-2", Guid.Parse("aaaaaaaa-0000-0000-0000-000000000012"));

        await publisher.PublishBatchAsync([env1, env2]);

        producer.Produced.Should().HaveCount(2);
        producer.Produced[0].Message.Key.Should().Be("electric-coelba:feat-1");
        producer.Produced[1].Message.Key.Should().Be("sanitation-aguas:feat-2");
    }

    [Fact]
    public async Task PublishAsync_TopicTemplateWithCatalogPlaceholder_ExpandsValidSlugAndRejectsInvalidSlug()
    {
        using var producer = new RecordingKafkaByteProducer();
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var serializer = new ProtobufIntegrationMessageSerializer();
        using var publisher = CreatePublisher(producer, tracer, topicTemplate: "geoassets.{catalogInstanceId}.events");

        var validEnvelope = CreateEnvelope(serializer, "electric-coelba", "feat-1", Guid.Parse("aaaaaaaa-0000-0000-0000-000000000021"));
        await publisher.PublishAsync(validEnvelope);

        producer.Produced.Should().ContainSingle()
            .Which.Topic.Should().Be("geoassets.electric-coelba.events");

        var invalidSlugEnvelope = validEnvelope with { CatalogInstanceId = "../invalid/slug" };
        var invalidSlugAct = () => publisher.PublishAsync(invalidSlugEnvelope);
        var slugEx = await invalidSlugAct.Should().ThrowAsync<Exception>();
        (slugEx.Which is ArgumentException or InvalidOperationException).Should().BeTrue();

        var missingCatalogEnvelope = validEnvelope with { CatalogInstanceId = null };
        var missingCatalogAct = () => publisher.PublishAsync(missingCatalogEnvelope);
        var missingEx = await missingCatalogAct.Should().ThrowAsync<Exception>();
        (missingEx.Which is ArgumentException or InvalidOperationException).Should().BeTrue();
    }

    [Fact]
    public async Task W3CDistributedTracing_ProducerToExtractEnvelopeToConsumer_PreservesTraceIdAndTraceState()
    {
        using var producer = new RecordingKafkaByteProducer();
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var timeProvider = new FakeTimeProvider(FixedUtc);
        var serializer = new ProtobufIntegrationMessageSerializer();
        using var publisher = CreatePublisher(producer, tracer, topicTemplate: "geoassets.catalog.events");

        var inbox = new LocalIdempotentInbox();
        var state = new HandlerRecordingState();
        await using var consumerHost = BuildConsumerProvider(
            timeProvider,
            tracer,
            serializer,
            inbox,
            state,
            targetCatalogInstanceId: "electric-coelba");
        var consumer = ResolveConsumer<FeatureCatalogMutatedEvent>(consumerHost);

        ActivityTraceId expectedTraceId;
        using (var rootSpan = TestSource.StartActivity("http.mutation.request", ActivityKind.Server))
        {
            rootSpan.Should().NotBeNull();
            rootSpan!.TraceStateString = "shard=electric-coelba";
            expectedTraceId = rootSpan.TraceId;

            var envelope = CreateEnvelope(serializer, "electric-coelba", "pole-9", Guid.Parse("aaaaaaaa-0000-0000-0000-000000000031"));
            await publisher.PublishAsync(envelope);
        }

        Activity.Current = null;
        var produced = producer.Produced.Should().ContainSingle().Subject;
        var consumeResult = new ConsumeResult<string?, byte[]>
        {
            Topic = produced.Topic,
            Partition = new Partition(0),
            Offset = new Offset(42),
            Message = produced.Message
        };

        var extracted = KafkaIntegrationEventConsumer<FeatureCatalogMutatedEvent>.ExtractEnvelope(
            consumeResult,
            timeProvider);

        extracted.MessageId.Should().Be(Guid.Parse("aaaaaaaa-0000-0000-0000-000000000031"));
        extracted.CatalogInstanceId.Should().Be("electric-coelba");
        extracted.TraceParent.Should().NotBeNullOrWhiteSpace();

        await consumer.ProcessEnvelopeAsync(extracted, CancellationToken.None);

        var invocation = state.Invocations.Should().ContainSingle().Subject;
        invocation.Event.FeatureId.Should().Be("pole-9");
        invocation.Context.CatalogInstanceId.Should().Be("electric-coelba");
        invocation.ObservedActivity.Should().NotBeNull();
        invocation.ObservedActivity!.Value.TraceId.Should().Be(expectedTraceId);
    }

    [Fact]
    public async Task PublishAsync_WhenCurrentActivityIsNull_RehydratesOutboxStoredTraceParentIntoKafkaHeader()
    {
        using var producer = new RecordingKafkaByteProducer();
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var serializer = new ProtobufIntegrationMessageSerializer();
        using var publisher = CreatePublisher(producer, tracer, topicTemplate: "geoassets.catalog.events");

        Activity.Current = null;
        const string storedTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        var envelope = CreateEnvelope(serializer, "electric-coelba", "pole-trace", Guid.Parse("aaaaaaaa-0000-0000-0000-000000000041")) with
        {
            TraceParent = $"00-{storedTraceId}-00f067aa0ba902b7-01",
            TraceState = "outbox=rehydrated"
        };

        await publisher.PublishAsync(envelope);

        var produced = producer.Produced.Should().ContainSingle().Subject;
        var traceParentHeader = ReadHeader(produced.Message.Headers, "traceparent");
        traceParentHeader.Should().NotBeNull();
        traceParentHeader.Should().Contain(storedTraceId);
    }

    [Fact]
    public async Task ProcessEnvelopeAsync_TargetCatalogInstanceIdShardFence_SkipsOtherCatalogWithZeroHandlerCalls()
    {
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var timeProvider = new FakeTimeProvider(FixedUtc);
        var serializer = new ProtobufIntegrationMessageSerializer();
        var inbox = new LocalIdempotentInbox();
        var state = new HandlerRecordingState();

        await using var consumerHost = BuildConsumerProvider(
            timeProvider,
            tracer,
            serializer,
            inbox,
            state,
            targetCatalogInstanceId: "electric-coelba");
        var consumer = ResolveConsumer<FeatureCatalogMutatedEvent>(consumerHost);

        var foreignShardEnvelope = CreateEnvelope(
            serializer,
            "sanitation-aguas",
            "pipe-77",
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000051"));

        await consumer.ProcessEnvelopeAsync(foreignShardEnvelope, CancellationToken.None);
        state.Invocations.Should().BeEmpty();

        var matchingShardEnvelope = CreateEnvelope(
            serializer,
            "electric-coelba",
            "pole-88",
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000052"));

        await consumer.ProcessEnvelopeAsync(matchingShardEnvelope, CancellationToken.None);
        state.Invocations.Should().ContainSingle()
            .Which.Event.FeatureId.Should().Be("pole-88");
    }

    [Fact]
    public async Task ProcessEnvelopeAsync_IdempotentInboxDeduplication_SkipsDuplicateReplay_AndRetriesAfterHandlerFailure()
    {
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var timeProvider = new FakeTimeProvider(FixedUtc);
        var serializer = new ProtobufIntegrationMessageSerializer();
        var inbox = new LocalIdempotentInbox();
        var state = new HandlerRecordingState { FailNextAttempt = true };

        await using var consumerHost = BuildConsumerProvider(
            timeProvider,
            tracer,
            serializer,
            inbox,
            state,
            targetCatalogInstanceId: "electric-coelba");
        var consumer = ResolveConsumer<FeatureCatalogMutatedEvent>(consumerHost);

        var envelope = CreateEnvelope(
            serializer,
            "electric-coelba",
            "pole-retry-1",
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000061"));

        var failAct = () => consumer.ProcessEnvelopeAsync(envelope, CancellationToken.None);
        await failAct.Should().ThrowAsync<InvalidOperationException>();
        state.Invocations.Should().BeEmpty();

        // Retry after transient failure must succeed because inbox did not mark failed attempt as processed.
        await consumer.ProcessEnvelopeAsync(envelope, CancellationToken.None);
        state.Invocations.Should().ContainSingle();

        // Duplicate delivery of the same MessageId must now be deduplicated.
        await consumer.ProcessEnvelopeAsync(envelope, CancellationToken.None);
        state.Invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task ProcessEnvelopeAsync_UnsupportedContentType_RejectsPoisonEnvelope()
    {
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var timeProvider = new FakeTimeProvider(FixedUtc);
        var serializer = new ProtobufIntegrationMessageSerializer();
        var inbox = new LocalIdempotentInbox();
        var state = new HandlerRecordingState();

        await using var consumerHost = BuildConsumerProvider(
            timeProvider,
            tracer,
            serializer,
            inbox,
            state,
            targetCatalogInstanceId: null);
        var consumer = ResolveConsumer<FeatureCatalogMutatedEvent>(consumerHost);

        var poisonEnvelope = CreateEnvelope(
            serializer,
            "electric-coelba",
            "pole-poison",
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000071")) with
        {
            ContentType = "application/xml",
            Payload = Encoding.UTF8.GetBytes("<corrupt/>")
        };

        var act = () => consumer.ProcessEnvelopeAsync(poisonEnvelope, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<Exception>();
        (ex.Which is FormatException or NotSupportedException or InvalidOperationException).Should().BeTrue();
        state.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_WhenKafkaProducerThrowsProduceException_PropagatesException()
    {
        using var producer = new RecordingKafkaByteProducer
        {
            OnProduce = () => throw new ProduceException<string?, byte[]>(
                new Error(ErrorCode.Local_Transport, "Broker transport failure"),
                new DeliveryResult<string?, byte[]>())
        };
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var serializer = new ProtobufIntegrationMessageSerializer();
        using var publisher = CreatePublisher(producer, tracer, topicTemplate: "geoassets.catalog.events");

        var envelope = CreateEnvelope(
            serializer,
            "electric-coelba",
            "pole-err",
            Guid.Parse("aaaaaaaa-0000-0000-0000-000000000081"));

        var act = () => publisher.PublishAsync(envelope);
        await act.Should().ThrowAsync<ProduceException<string?, byte[]>>();
    }

    [Fact]
    public void DependencyInjection_AddKafkaIntegrationPublisherAndConsumer_RegistersServices()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["WorkflowKafka:BootstrapServers"] = "localhost:9092",
                ["WorkflowKafka:TopicName"] = "geoassets.events"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(FixedUtc));
        services.AddSingleton(new GeoAssetsActivitySource("1.0.0"));
        services.AddWorkflowProtobufMessaging();
        services.AddSingleton(new HandlerRecordingState());
        services.AddKafkaIntegrationPublisher(config);
        services.AddKafkaIntegrationConsumer<FeatureCatalogMutatedEvent, ForwardingFeatureHandler>(opts =>
        {
            opts.TargetCatalogInstanceId = "electric-coelba";
        });

        services.Should().Contain(d => d.ServiceType == typeof(IIntegrationEventPublisher));
        services.Should().Contain(d => d.ServiceType == typeof(IHostedService));
    }

    private static KafkaIntegrationEventPublisher CreatePublisher(
        IProducer<string?, byte[]> producer,
        GeoAssetsActivitySource tracer,
        string topicTemplate)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(FixedUtc));
        services.AddSingleton(tracer);
        services.AddWorkflowProtobufMessaging();
        using var sp = services.BuildServiceProvider();

        var ctor = typeof(KafkaIntegrationEventPublisher)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .First(c => c.GetParameters().Any(p => p.ParameterType == typeof(IProducer<string?, byte[]>)));

        var args = ctor.GetParameters()
            .Select(p =>
            {
                if (p.ParameterType == typeof(IProducer<string?, byte[]>))
                {
                    return (object)producer;
                }

                var resolved = sp.GetRequiredService(p.ParameterType);
                if (p.ParameterType.IsGenericType && p.ParameterType.GetGenericTypeDefinition() == typeof(IOptions<>))
                {
                    var valueProp = p.ParameterType.GetProperty("Value")!;
                    var optsInstance = valueProp.GetValue(resolved)!;
                    ConfigureTopicProperty(optsInstance, topicTemplate);
                }

                return resolved;
            })
            .ToArray();

        return (KafkaIntegrationEventPublisher)ctor.Invoke(args);
    }

    private static void ConfigureTopicProperty(object optionsInstance, string topicTemplate)
    {
        var type = optionsInstance.GetType();
        var prop = type.GetProperty("TopicTemplate")
            ?? type.GetProperty("TopicName")
            ?? type.GetProperty("DefaultTopic");
        prop?.SetValue(optionsInstance, topicTemplate);
    }

    private static ServiceProvider BuildConsumerProvider(
        TimeProvider timeProvider,
        GeoAssetsActivitySource tracer,
        IIntegrationMessageSerializer serializer,
        IIdempotentInbox inbox,
        HandlerRecordingState state,
        string? targetCatalogInstanceId)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(timeProvider);
        services.AddSingleton(tracer);
        services.AddSingleton(serializer);
        services.AddScoped<IIdempotentInbox>(_ => inbox);
        services.AddSingleton(state);
        services.AddKafkaIntegrationConsumer<FeatureCatalogMutatedEvent, ForwardingFeatureHandler>(opts =>
        {
            opts.TargetCatalogInstanceId = targetCatalogInstanceId;
        });

        return services.BuildServiceProvider();
    }

    private static KafkaIntegrationEventConsumer<TEvent> ResolveConsumer<TEvent>(IServiceProvider provider)
        where TEvent : notnull
    {
        var direct = provider.GetService<KafkaIntegrationEventConsumer<TEvent>>();
        if (direct is not null)
        {
            return direct;
        }

        var hosted = provider.GetServices<IHostedService>()
            .OfType<KafkaIntegrationEventConsumer<TEvent>>()
            .FirstOrDefault();
        if (hosted is not null)
        {
            return hosted;
        }

        return ActivatorUtilities.CreateInstance<KafkaIntegrationEventConsumer<TEvent>>(provider);
    }

    private static string? ReadHeader(Headers? headers, string key)
    {
        if (headers is null)
        {
            return null;
        }

        var match = headers.LastOrDefault(h => string.Equals(h.Key, key, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : Encoding.UTF8.GetString(match.GetValueBytes());
    }

    private static IntegrationEnvelope CreateEnvelope(
        ProtobufIntegrationMessageSerializer serializer,
        string catalogInstanceId,
        string featureId,
        Guid messageId)
    {
        var evt = CreateMutatedEvent(catalogInstanceId, featureId);
        return new IntegrationEnvelope(
            MessageId: messageId,
            MessageType: IntegrationEventTypes.FeatureCatalogMutatedV1,
            SchemaVersion: IntegrationEventTypes.CurrentSchemaVersion,
            PartitionKey: $"{catalogInstanceId}:{featureId}",
            ContentType: serializer.ContentType,
            Payload: serializer.Serialize(evt),
            OccurredAtUtc: FixedUtc,
            CatalogInstanceId: catalogInstanceId,
            SourceService: "geoassets-server",
            CorrelationId: $"corr-{featureId}");
    }

    private static FeatureCatalogMutatedEvent CreateMutatedEvent(string catalogInstanceId, string featureId) =>
        new(
            CatalogInstanceId: catalogInstanceId,
            OrganizationId: SampleOrgId,
            ScopeId: "zone-1",
            FeatureId: featureId,
            MutationKind: AssetMutationKind.UpsertFeature,
            Name: $"Asset {featureId}",
            AssetTypeId: "pole",
            LayerId: "layer-poles",
            Geometry: new AssetGeometrySnapshot(WireGeometryKind.Point, 4326, [-38.5014, -12.9714], [], []),
            CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal) { ["voltage"] = "13.8kV" },
            AffectedBounds: new Rfc7946BoundingBox(-38.51, -12.98, -38.50, -12.97),
            RowVersion: [0x01, 0x02, 0x03, 0x04],
            MutatedAtUtc: FixedUtc,
            CorrelationId: $"corr-{featureId}");

    private sealed class HandlerRecordingState
    {
        public List<(FeatureCatalogMutatedEvent Event, IntegrationMessageContext Context, ActivityContext? ObservedActivity)> Invocations { get; } = [];

        public bool FailNextAttempt { get; set; }
    }

    private sealed class ForwardingFeatureHandler(HandlerRecordingState state)
        : IIntegrationEventHandler<FeatureCatalogMutatedEvent>
    {
        public Task HandleAsync(
            FeatureCatalogMutatedEvent message,
            IntegrationMessageContext context,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (state.FailNextAttempt)
            {
                state.FailNextAttempt = false;
                throw new InvalidOperationException("Simulated transient handler failure");
            }

            state.Invocations.Add((message, context, Activity.Current?.Context));
            return Task.CompletedTask;
        }
    }

    private sealed class LocalIdempotentInbox : IIdempotentInbox
    {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);

        public Task<bool> HasProcessedAsync(Guid messageId, string consumerName, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_keys.Contains($"{messageId:D}:{consumerName}"));
        }

        public Task<bool> TryRecordProcessedAsync(
            Guid messageId,
            string consumerName,
            string messageType,
            TimeSpan retentionPeriod,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_keys.Add($"{messageId:D}:{consumerName}"));
        }

        public Task<int> PurgeExpiredAsync(int maxBatchSize, CancellationToken ct = default) =>
            Task.FromResult(0);
    }

    private sealed class RecordingKafkaByteProducer : IProducer<string?, byte[]>
    {
        public List<(string Topic, Message<string?, byte[]> Message)> Produced { get; } = [];

        public Action? OnProduce { get; init; }

        public Handle Handle => null!;

        public string Name => "recording-kafka-byte-producer";

        public Task<DeliveryResult<string?, byte[]>> ProduceAsync(
            string topic,
            Message<string?, byte[]> message,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OnProduce?.Invoke();
            Produced.Add((topic, message));

            return Task.FromResult(new DeliveryResult<string?, byte[]>
            {
                Topic = topic,
                Partition = new Partition(0),
                Offset = new Offset(Produced.Count - 1),
                Message = message,
                Status = PersistenceStatus.Persisted
            });
        }

        public Task<DeliveryResult<string?, byte[]>> ProduceAsync(
            TopicPartition topicPartition,
            Message<string?, byte[]> message,
            CancellationToken cancellationToken = default) =>
            ProduceAsync(topicPartition.Topic, message, cancellationToken);

        public void Produce(
            string topic,
            Message<string?, byte[]> message,
            Action<DeliveryReport<string?, byte[]>>? deliveryHandler = null)
        {
            OnProduce?.Invoke();
            Produced.Add((topic, message));
            deliveryHandler?.Invoke(new DeliveryReport<string?, byte[]>
            {
                Topic = topic,
                Partition = new Partition(0),
                Offset = new Offset(Produced.Count - 1),
                Message = message,
                Status = PersistenceStatus.Persisted
            });
        }

        public void Produce(
            TopicPartition topicPartition,
            Message<string?, byte[]> message,
            Action<DeliveryReport<string?, byte[]>>? deliveryHandler = null) =>
            Produce(topicPartition.Topic, message, deliveryHandler);

        public int Poll(TimeSpan timeout) => 0;

        public int Flush(TimeSpan timeout) => 0;

        public void Flush(CancellationToken cancellationToken = default) =>
            cancellationToken.ThrowIfCancellationRequested();

        public void InitTransactions(TimeSpan timeout)
        {
        }

        public void BeginTransaction()
        {
        }

        public void CommitTransaction(TimeSpan timeout)
        {
        }

        public void CommitTransaction()
        {
        }

        public void AbortTransaction(TimeSpan timeout)
        {
        }

        public void AbortTransaction()
        {
        }

        public void SendOffsetsToTransaction(
            IEnumerable<TopicPartitionOffset> offsets,
            IConsumerGroupMetadata groupMetadata,
            TimeSpan timeout)
        {
        }

        public int AddBrokers(string brokers) => 1;

        public void SetSaslCredentials(string username, string password)
        {
        }

        public void Dispose()
        {
        }
    }
}
