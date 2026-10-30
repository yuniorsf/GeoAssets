using System.Diagnostics;
using System.Reflection;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Messaging.Protobuf;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Messaging.ServiceBus.Tests;

public sealed class ServiceBusIntegrationMessagingTests
{
    private static readonly DateTimeOffset FixedUtc = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SampleOrgId = Guid.Parse("11111111-2222-3333-4444-555555555555");
    private static readonly ActivitySource TestSource = new("GeoAssets.Workflow.Messaging.ServiceBus.Tests.Integration");

    static ServiceBusIntegrationMessagingTests()
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
    public async Task PublishAsync_BinaryProtobufEnvelope_SendsServiceBusMessageWithCatalogApplicationProperty()
    {
        await using var sender = new RecordingServiceBusSender("geoassets-catalog-events");
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var serializer = new ProtobufIntegrationMessageSerializer();
        await using var publisher = CreatePublisher(sender, tracer, topicTemplate: "geoassets-catalog-events");

        var domainEvent = CreateMutatedEvent("electric-coelba", "pole-ba-100");
        var payload = serializer.Serialize(domainEvent);
        var messageId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001");
        var envelope = new IntegrationEnvelope(
            MessageId: messageId,
            MessageType: IntegrationEventTypes.FeatureCatalogMutatedV1,
            SchemaVersion: IntegrationEventTypes.CurrentSchemaVersion,
            PartitionKey: "electric-coelba:pole-ba-100",
            ContentType: serializer.ContentType,
            Payload: payload,
            OccurredAtUtc: FixedUtc,
            CatalogInstanceId: "electric-coelba",
            SourceService: "geoassets-server",
            CorrelationId: "corr-sb-1");

        await publisher.PublishAsync(envelope);

        var sent = sender.SentMessages.Should().ContainSingle().Subject;
        sent.ContentType.Should().Be("application/x-protobuf");
        sent.Body.ToArray().Should().Equal(payload);
        sent.CorrelationId.Should().Be("corr-sb-1");
        sent.ApplicationProperties.Should().ContainKey("catalogInstanceId")
            .WhoseValue.Should().Be("electric-coelba");

        var decoded = serializer.Deserialize<FeatureCatalogMutatedEvent>(sent.Body.ToMemory());
        decoded.CatalogInstanceId.Should().Be("electric-coelba");
        decoded.FeatureId.Should().Be("pole-ba-100");
    }

    [Fact]
    public async Task PublishBatchAsync_SendsAllEnvelopesAcrossCatalogShards()
    {
        await using var sender = new RecordingServiceBusSender("geoassets-catalog-events");
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var serializer = new ProtobufIntegrationMessageSerializer();
        await using var publisher = CreatePublisher(sender, tracer, topicTemplate: "geoassets-catalog-events");

        var env1 = CreateEnvelope(serializer, "electric-coelba", "pole-1", Guid.Parse("bbbbbbbb-0000-0000-0000-000000000011"));
        var env2 = CreateEnvelope(serializer, "sanitation-aguas", "pipe-2", Guid.Parse("bbbbbbbb-0000-0000-0000-000000000012"));

        await publisher.PublishBatchAsync([env1, env2]);

        sender.SentMessages.Should().HaveCount(2);
        sender.SentMessages[0].ApplicationProperties["catalogInstanceId"].Should().Be("electric-coelba");
        sender.SentMessages[1].ApplicationProperties["catalogInstanceId"].Should().Be("sanitation-aguas");
    }

    [Fact]
    public void ResolveTopicName_WithCatalogInstanceIdPlaceholder_ExpandsValidSlugAndRejectsInvalidSlug()
    {
        var sender = new RecordingServiceBusSender("geoassets-{catalogInstanceId}-events");
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var serializer = new ProtobufIntegrationMessageSerializer();
        var publisher = CreatePublisher(sender, tracer, topicTemplate: "geoassets-{catalogInstanceId}-events");

        var validEnvelope = CreateEnvelope(serializer, "electric-coelba", "pole-1", Guid.Parse("bbbbbbbb-0000-0000-0000-000000000021"));
        var resolved = InvokeResolveTopicName(publisher, validEnvelope, "geoassets-{catalogInstanceId}-events");
        resolved.Should().Be("geoassets-electric-coelba-events");

        var invalidEnvelope = validEnvelope with { CatalogInstanceId = "../bad-topic" };
        var act = () => InvokeResolveTopicName(publisher, invalidEnvelope, "geoassets-{catalogInstanceId}-events");
        var ex = act.Should().Throw<Exception>().Which;
        (ex is ArgumentException or InvalidOperationException).Should().BeTrue();
    }

    [Fact]
    public async Task W3CDistributedTracing_ProducerToProcessReceivedMessageAsync_PreservesTraceId()
    {
        await using var sender = new RecordingServiceBusSender("geoassets-catalog-events");
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var timeProvider = new FakeTimeProvider(FixedUtc);
        var serializer = new ProtobufIntegrationMessageSerializer();
        await using var publisher = CreatePublisher(sender, tracer, topicTemplate: "geoassets-catalog-events");

        var inbox = new LocalIdempotentInbox();
        var state = new HandlerRecordingState();
        await using var consumerHost = BuildConsumerProvider(
            timeProvider,
            tracer,
            serializer,
            inbox,
            state,
            targetCatalogInstanceId: "electric-coelba",
            deadLetterOnCatalogMismatch: false);
        var consumer = ResolveConsumer<FeatureCatalogMutatedEvent>(consumerHost);

        ActivityTraceId expectedTraceId;
        using (var rootSpan = TestSource.StartActivity("http.CatalogMutation", ActivityKind.Server))
        {
            rootSpan.Should().NotBeNull();
            rootSpan!.TraceStateString = "shard=electric-coelba";
            expectedTraceId = rootSpan.TraceId;

            var envelope = CreateEnvelope(serializer, "electric-coelba", "pole-trace", Guid.Parse("bbbbbbbb-0000-0000-0000-000000000031"));
            await publisher.PublishAsync(envelope);
        }

        Activity.Current = null;
        var sent = sender.SentMessages.Should().ContainSingle().Subject;
        var received = ToReceivedMessage(sent);

        var disposition = await consumer.ProcessReceivedMessageAsync(received, CancellationToken.None);

        disposition.Should().Be(ServiceBusConsumeDisposition.Complete);
        var invocation = state.Invocations.Should().ContainSingle().Subject;
        invocation.Event.FeatureId.Should().Be("pole-trace");
        invocation.ObservedActivity.Should().NotBeNull();
        invocation.ObservedActivity!.Value.TraceId.Should().Be(expectedTraceId);
    }

    [Fact]
    public async Task ProcessReceivedMessageAsync_ExercisesAllFiveDispositions_Complete_DuplicateSkipped_AbandonTransient_DeadLetterPoison_CatalogShardFenced()
    {
        await using var sender = new RecordingServiceBusSender("geoassets-catalog-events");
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var timeProvider = new FakeTimeProvider(FixedUtc);
        var serializer = new ProtobufIntegrationMessageSerializer();
        await using var publisher = CreatePublisher(sender, tracer, topicTemplate: "geoassets-catalog-events");

        var inbox = new LocalIdempotentInbox();
        var state = new HandlerRecordingState();
        await using var consumerHost = BuildConsumerProvider(
            timeProvider,
            tracer,
            serializer,
            inbox,
            state,
            targetCatalogInstanceId: "electric-coelba",
            deadLetterOnCatalogMismatch: false);
        var consumer = ResolveConsumer<FeatureCatalogMutatedEvent>(consumerHost);

        // 1. Transient failure -> AbandonTransient (and inbox must not mark processed)
        var validEnvelope = CreateEnvelope(serializer, "electric-coelba", "pole-50", Guid.Parse("bbbbbbbb-0000-0000-0000-000000000051"));
        await publisher.PublishAsync(validEnvelope);
        var validReceived = ToReceivedMessage(sender.SentMessages[^1]);

        state.FailNextAttempt = true;
        var transientDisposition = await consumer.ProcessReceivedMessageAsync(validReceived, CancellationToken.None);
        transientDisposition.Should().Be(ServiceBusConsumeDisposition.AbandonTransient);
        state.Invocations.Should().BeEmpty();

        // 2. Retry succeeds -> Complete
        var completeDisposition = await consumer.ProcessReceivedMessageAsync(validReceived, CancellationToken.None);
        completeDisposition.Should().Be(ServiceBusConsumeDisposition.Complete);
        state.Invocations.Should().ContainSingle();

        // 3. Duplicate replay -> DuplicateSkipped
        var duplicateDisposition = await consumer.ProcessReceivedMessageAsync(validReceived, CancellationToken.None);
        duplicateDisposition.Should().Be(ServiceBusConsumeDisposition.DuplicateSkipped);
        state.Invocations.Should().ContainSingle();

        // 4. Foreign catalog shard with DeadLetterOnCatalogMismatch = false -> CatalogShardFenced
        var foreignEnvelope = CreateEnvelope(serializer, "sanitation-aguas", "pipe-99", Guid.Parse("bbbbbbbb-0000-0000-0000-000000000052"));
        await publisher.PublishAsync(foreignEnvelope);
        var foreignReceived = ToReceivedMessage(sender.SentMessages[^1]);

        var fencedDisposition = await consumer.ProcessReceivedMessageAsync(foreignReceived, CancellationToken.None);
        fencedDisposition.Should().Be(ServiceBusConsumeDisposition.CatalogShardFenced);
        state.Invocations.Should().ContainSingle();

        // 5. Corrupt Protobuf wire bytes -> DeadLetterPoison
        var poisonProperties = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["messageType"] = IntegrationEventTypes.FeatureCatalogMutatedV1,
            ["schemaVersion"] = IntegrationEventTypes.CurrentSchemaVersion,
            ["catalogInstanceId"] = "electric-coelba"
        };
        var poisonReceived = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F, 0x0B }),
            messageId: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000053").ToString("D", System.Globalization.CultureInfo.InvariantCulture),
            contentType: serializer.ContentType,
            properties: poisonProperties,
            enqueuedTime: FixedUtc);

        var poisonDisposition = await consumer.ProcessReceivedMessageAsync(poisonReceived, CancellationToken.None);
        poisonDisposition.Should().Be(ServiceBusConsumeDisposition.DeadLetterPoison);
        state.Invocations.Should().ContainSingle();
    }

    [Fact]
    public async Task ProcessReceivedMessageAsync_WhenDeadLetterOnCatalogMismatchEnabled_DeadLettersMismatchedShard()
    {
        await using var sender = new RecordingServiceBusSender("geoassets-catalog-events");
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var timeProvider = new FakeTimeProvider(FixedUtc);
        var serializer = new ProtobufIntegrationMessageSerializer();
        await using var publisher = CreatePublisher(sender, tracer, topicTemplate: "geoassets-catalog-events");

        var inbox = new LocalIdempotentInbox();
        var state = new HandlerRecordingState();
        await using var consumerHost = BuildConsumerProvider(
            timeProvider,
            tracer,
            serializer,
            inbox,
            state,
            targetCatalogInstanceId: "electric-coelba",
            deadLetterOnCatalogMismatch: true);
        var consumer = ResolveConsumer<FeatureCatalogMutatedEvent>(consumerHost);

        var foreignEnvelope = CreateEnvelope(serializer, "sanitation-aguas", "pipe-deadletter", Guid.Parse("bbbbbbbb-0000-0000-0000-000000000061"));
        await publisher.PublishAsync(foreignEnvelope);
        var foreignReceived = ToReceivedMessage(sender.SentMessages[^1]);

        var disposition = await consumer.ProcessReceivedMessageAsync(foreignReceived, CancellationToken.None);
        disposition.Should().Be(ServiceBusConsumeDisposition.DeadLetterPoison);
        state.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_WhenServiceBusSenderThrowsServiceBusException_PropagatesException()
    {
        await using var sender = new RecordingServiceBusSender("geoassets-catalog-events")
        {
            OnSend = () => throw new ServiceBusException("Service Bus namespace busy", ServiceBusFailureReason.ServiceBusy)
        };
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        var serializer = new ProtobufIntegrationMessageSerializer();
        await using var publisher = CreatePublisher(sender, tracer, topicTemplate: "geoassets-catalog-events");

        var envelope = CreateEnvelope(serializer, "electric-coelba", "pole-err", Guid.Parse("bbbbbbbb-0000-0000-0000-000000000071"));

        var act = () => publisher.PublishAsync(envelope);
        await act.Should().ThrowAsync<ServiceBusException>();
    }

    [Fact]
    public void DependencyInjection_AddServiceBusIntegrationPublisherAndConsumer_RegistersServices()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["WorkflowServiceBus:ConnectionString"] = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=Root;SharedAccessKey=YWJj",
                ["WorkflowServiceBus:TopicName"] = "geoassets-events"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(FixedUtc));
        services.AddSingleton(new GeoAssetsActivitySource("1.0.0"));
        services.AddWorkflowProtobufMessaging();
        services.AddSingleton(new HandlerRecordingState());
        services.AddServiceBusIntegrationPublisher(config);
        services.AddServiceBusIntegrationConsumer<FeatureCatalogMutatedEvent, ForwardingFeatureHandler>(opts =>
        {
            opts.TargetCatalogInstanceId = "electric-coelba";
            opts.DeadLetterOnCatalogMismatch = true;
        });

        services.Should().Contain(d => d.ServiceType == typeof(IIntegrationEventPublisher));
        services.Should().Contain(d => d.ServiceType == typeof(IHostedService));
    }

    private static ServiceBusIntegrationEventPublisher CreatePublisher(
        ServiceBusSender sender,
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

        var ctor = typeof(ServiceBusIntegrationEventPublisher)
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .First(c => c.GetParameters().Any(p => p.ParameterType == typeof(ServiceBusSender)));

        var args = ctor.GetParameters()
            .Select(p =>
            {
                if (p.ParameterType == typeof(ServiceBusSender))
                {
                    return (object)sender;
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

        return (ServiceBusIntegrationEventPublisher)ctor.Invoke(args);
    }

    private static void ConfigureTopicProperty(object optionsInstance, string topicTemplate)
    {
        var type = optionsInstance.GetType();
        var prop = type.GetProperty("TopicTemplate")
            ?? type.GetProperty("TopicName")
            ?? type.GetProperty("DefaultTopic");
        prop?.SetValue(optionsInstance, topicTemplate);
    }

    private static string InvokeResolveTopicName(
        ServiceBusIntegrationEventPublisher publisher,
        IntegrationEnvelope envelope,
        string topicTemplate)
    {
        var method = typeof(ServiceBusIntegrationEventPublisher)
            .GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .First(m => string.Equals(m.Name, "ResolveTopicName", StringComparison.Ordinal));

        try
        {
            var parameters = method.GetParameters();
            object? result = parameters.Length switch
            {
                1 when parameters[0].ParameterType == typeof(IntegrationEnvelope) =>
                    method.Invoke(method.IsStatic ? null : publisher, [envelope]),
                2 when parameters[0].ParameterType == typeof(string) =>
                    method.Invoke(method.IsStatic ? null : publisher, [topicTemplate, envelope.CatalogInstanceId]),
                2 when parameters[0].ParameterType == typeof(IntegrationEnvelope) =>
                    method.Invoke(method.IsStatic ? null : publisher, [envelope, topicTemplate]),
                _ => throw new InvalidOperationException("Unexpected ResolveTopicName signature.")
            };

            return (string)result!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            throw ex.InnerException;
        }
    }

    private static ServiceProvider BuildConsumerProvider(
        TimeProvider timeProvider,
        GeoAssetsActivitySource tracer,
        IIntegrationMessageSerializer serializer,
        IIdempotentInbox inbox,
        HandlerRecordingState state,
        string? targetCatalogInstanceId,
        bool deadLetterOnCatalogMismatch)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(timeProvider);
        services.AddSingleton(tracer);
        services.AddSingleton(serializer);
        services.AddScoped<IIdempotentInbox>(_ => inbox);
        services.AddSingleton(state);
        services.AddServiceBusIntegrationConsumer<FeatureCatalogMutatedEvent, ForwardingFeatureHandler>(opts =>
        {
            opts.TargetCatalogInstanceId = targetCatalogInstanceId;
            opts.DeadLetterOnCatalogMismatch = deadLetterOnCatalogMismatch;
        });

        return services.BuildServiceProvider();
    }

    private static ServiceBusIntegrationEventConsumer<TEvent> ResolveConsumer<TEvent>(IServiceProvider provider)
        where TEvent : notnull
    {
        var direct = provider.GetService<ServiceBusIntegrationEventConsumer<TEvent>>();
        if (direct is not null)
        {
            return direct;
        }

        var hosted = provider.GetServices<IHostedService>()
            .OfType<ServiceBusIntegrationEventConsumer<TEvent>>()
            .FirstOrDefault();
        if (hosted is not null)
        {
            return hosted;
        }

        return ActivatorUtilities.CreateInstance<ServiceBusIntegrationEventConsumer<TEvent>>(provider);
    }

    private static ServiceBusReceivedMessage ToReceivedMessage(ServiceBusMessage sent) =>
        ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: sent.Body,
            messageId: sent.MessageId,
            partitionKey: sent.PartitionKey,
            correlationId: sent.CorrelationId,
            subject: sent.Subject,
            contentType: sent.ContentType,
            properties: sent.ApplicationProperties,
            enqueuedTime: FixedUtc);

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

    private sealed class RecordingServiceBusSender(string entityPath) : ServiceBusSender
    {
        public List<ServiceBusMessage> SentMessages { get; } = [];

        public Action? OnSend { get; init; }

        public override string EntityPath => entityPath;

        public override ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        public override ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync(
            CreateMessageBatchOptions options,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return default;
        }

        public override Task SendMessageAsync(
            ServiceBusMessage message,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(message);
            cancellationToken.ThrowIfCancellationRequested();
            OnSend?.Invoke();
            SentMessages.Add(message);
            return Task.CompletedTask;
        }

        public override Task SendMessagesAsync(
            IEnumerable<ServiceBusMessage> messages,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(messages);
            cancellationToken.ThrowIfCancellationRequested();
            OnSend?.Invoke();
            SentMessages.AddRange(messages);
            return Task.CompletedTask;
        }

        public override Task CloseAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public override ValueTask DisposeAsync() => base.DisposeAsync();
    }
}
