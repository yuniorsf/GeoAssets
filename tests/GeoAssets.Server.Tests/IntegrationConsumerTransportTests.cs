using System.Reflection;
using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Messaging.Kafka;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Messaging.ServiceBus;
using GeoAssets.Workflow.Persistence.Outbox;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// How a host consumes the messages a vertical or the kernel names (XD01-383), <see cref="IntegrationConsumerHostExtensions"/>:
/// no broker — an in-process route fed by an outbox relay that claims only the routed message types; a broker with a topic for
/// the consumer — a named Kafka or Service Bus consumer of its own group or subscription, for its message type only; a broker
/// with no topic for it — nothing. The consumers themselves skip a type they do not handle (Kafka commits it, Service Bus
/// completes it).
/// </summary>
public sealed class IntegrationConsumerTransportTests
{
    private const string Key = "AssetMutationApply";
    private const string Name = "kernel.asset-mutation-apply";
    private const string Type = IntegrationEventTypes.AssetMutationBatchRequestedV1;

    private static readonly IntegrationConsumerRegistration Registration = new(Key, Name, Type) { TargetCatalogInstanceId = "kernel-catalog" };

    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))).Build();

    private static (string, string?) Transport(string? transport) => (WorkflowMessagingHostExtensions.OutboxTransportKey, transport);

    // ── Which transport ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("None")]
    [InlineData("none")]
    public void TransportFor_WithNoBroker_IsInProcess(string? transport)
    {
        IntegrationConsumerHostExtensions.TransportFor(Configuration(Transport(transport)), Registration).Should().Be(IntegrationConsumerTransport.InProcess);
    }

    [Fact]
    public void TransportFor_KafkaWithoutATopicForTheConsumer_IsNone_ARegisteredConsumerWouldNotRun()
    {
        IntegrationConsumerHostExtensions.TransportFor(Configuration(Transport("Kafka")), Registration).Should().Be(IntegrationConsumerTransport.None);
    }

    [Fact]
    public void TransportFor_KafkaWithTheSharedTopic_IsKafka()
    {
        var configuration = Configuration(Transport("Kafka"), ("WorkflowKafkaConsumer:TopicName", "geoassets.events"));

        IntegrationConsumerHostExtensions.TransportFor(configuration, Registration).Should().Be(IntegrationConsumerTransport.Kafka);
    }

    [Fact]
    public void TransportFor_KafkaWithATopicOnlyForAnotherConsumer_IsNoneForThisOne()
    {
        var configuration = Configuration(Transport("Kafka"), ("WorkflowKafkaConsumer:Consumers:OtherConsumer:TopicName", "other"));

        IntegrationConsumerHostExtensions.TransportFor(configuration, Registration).Should().Be(IntegrationConsumerTransport.None);
        IntegrationConsumerHostExtensions.TransportFor(configuration, Registration with { ConsumerKey = "OtherConsumer" }).Should().Be(IntegrationConsumerTransport.Kafka);
    }

    [Fact]
    public void TransportFor_ServiceBusWithATopicAndASubscription_IsServiceBus()
    {
        var configuration = Configuration(
            Transport("ServiceBus"), ("WorkflowServiceBusConsumer:TopicName", "geoassets.events"),
            ($"WorkflowServiceBusConsumer:Consumers:{Key}:SubscriptionName", "kernel-apply"));

        IntegrationConsumerHostExtensions.TransportFor(configuration, Registration).Should().Be(IntegrationConsumerTransport.ServiceBus);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void TransportFor_ServiceBusWithoutATopic_IsNone(bool topic, bool subscription)
    {
        var settings = new List<(string, string?)> { Transport("ServiceBus") };
        if (topic) settings.Add(("WorkflowServiceBusConsumer:TopicName", "geoassets.events"));
        if (subscription) settings.Add(($"WorkflowServiceBusConsumer:Consumers:{Key}:SubscriptionName", "kernel-apply"));

        IntegrationConsumerHostExtensions.TransportFor(Configuration([.. settings]), Registration).Should().Be(IntegrationConsumerTransport.None);
    }

    [Fact]
    public void AddWorkflowIntegrationConsumer_ServiceBusWithATopicButNoSubscription_FailsClearly()
    {
        var configuration = Configuration(Transport("ServiceBus"), ("WorkflowServiceBusConsumer:TopicName", "geoassets.events"));
        var services = new ServiceCollection();

        var act = () => services.AddWorkflowIntegrationConsumer<AssetMutationBatchRequestedEvent, NoopHandler>(configuration, Registration);

        act.Should().Throw<InvalidOperationException>().WithMessage("*SubscriptionName*");
    }

    [Fact]
    public void TransportFor_NullArguments_Throw()
    {
        ((Action)(() => IntegrationConsumerHostExtensions.TransportFor(null!, Registration))).Should().Throw<ArgumentNullException>();
        ((Action)(() => IntegrationConsumerHostExtensions.TransportFor(Configuration(), null!))).Should().Throw<ArgumentNullException>();
    }

    // ── In process ────────────────────────────────────────────────────────────

    [Fact]
    public void AddWorkflowIntegrationConsumer_WithNoBroker_RegistersAnInProcessRoute_AndARelayThatClaimsOnlyTheRoutedTypes()
    {
        var services = new ServiceCollection();

        services.AddWorkflowIntegrationConsumer<AssetMutationBatchRequestedEvent, NoopHandler>(Configuration(), Registration);
        services.AddWorkflowIntegrationConsumer<AssetBatchAppliedEvent, AppliedHandler>(
            Configuration(), new IntegrationConsumerRegistration("Applied", "neutral-operator.applied", IntegrationEventTypes.AssetMutationBatchAppliedV1));
        using var provider = services.BuildServiceProvider();

        var routes = provider.GetServices<InProcessIntegrationRoute>().ToList();
        routes.Select(r => (r.MessageType, r.ConsumerName)).Should().BeEquivalentTo(
            [(Type, Name), (IntegrationEventTypes.AssetMutationBatchAppliedV1, "neutral-operator.applied")]);
        var options = provider.GetRequiredService<IOptions<OutboxRelayOptions>>().Value;
        options.MessageTypes.Should().BeEquivalentTo(Type, IntegrationEventTypes.AssetMutationBatchAppliedV1);
        options.WorkerId.Should().Be(IntegrationConsumerHostExtensions.InProcessRelayWorkerId);
        services.Count(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(OutboxRelayProcessor)).Should().Be(1, "one relay for every route");
        services.Should().Contain(d => d.ServiceType == typeof(IIntegrationEventHandler<AssetMutationBatchRequestedEvent>) && d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddWorkflowIntegrationConsumer_RegisteringTheSameTypeTwice_ClaimsItOnce()
    {
        var services = new ServiceCollection();
        services.AddWorkflowIntegrationConsumer<AssetMutationBatchRequestedEvent, NoopHandler>(Configuration(), Registration);
        services.AddWorkflowIntegrationConsumer<AssetMutationBatchRequestedEvent, NoopHandler>(Configuration(), Registration with { ConsumerName = "second" });
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOptions<OutboxRelayOptions>>().Value.MessageTypes.Should().ContainSingle().Which.Should().Be(Type);
        provider.GetServices<InProcessIntegrationRoute>().Should().HaveCount(2, "each consumer keeps its own route and inbox name");
    }

    [Fact]
    public async Task InProcessPublisher_DeliversAMessageToEveryRouteOfItsType_EachWithItsOwnInboxRecord_AndAnotherTypeToNone()
    {
        var harness = InProcessHarness();
        var envelope = Envelope(Type);

        await harness.Publisher.PublishAsync(envelope);

        harness.Handler.Invocations.Should().HaveCount(2, "two routes consume the type");
        harness.Inbox.RecordedEntries.Select(e => e.ConsumerName).Should().BeEquivalentTo("consumer-a", "consumer-b");
        harness.Publisher.MessageTypes.Should().Equal(Type);
        var other = () => harness.Publisher.PublishAsync(Envelope(IntegrationEventTypes.OrderStateChangedV1));
        await other.Should().ThrowAsync<NotSupportedException>().WithMessage($"*'{IntegrationEventTypes.OrderStateChangedV1}'*");
    }

    [Fact]
    public async Task InProcessPublisher_AMessageAlreadyInTheInbox_IsNotHandledAgain()
    {
        var harness = InProcessHarness();
        var envelope = Envelope(Type);
        await harness.Publisher.PublishAsync(envelope);

        await harness.Publisher.PublishAsync(envelope);

        harness.Handler.Invocations.Should().HaveCount(2, "the second delivery of each route was a duplicate");
    }

    [Fact]
    public async Task InProcessPublisher_AFailingHandler_PropagatesAndRecordsNothing_SoTheRelayRetries()
    {
        var harness = InProcessHarness();
        harness.Handler.OnHandleAsync = (_, _, _) => throw new TimeoutException("handler failed");

        var act = () => harness.Publisher.PublishAsync(Envelope(Type));

        await act.Should().ThrowAsync<TimeoutException>();
        harness.Inbox.RecordedEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task InProcessPublisher_PublishBatchAsync_DeliversInOrder()
    {
        var harness = InProcessHarness();

        await harness.Publisher.PublishBatchAsync([Envelope(Type), Envelope(Type)]);

        harness.Handler.Invocations.Should().HaveCount(4);
    }

    // ── Brokers ───────────────────────────────────────────────────────────────

    [Fact]
    public void AddWorkflowIntegrationConsumer_KafkaWithATopic_RegistersOneNamedConsumer_InItsOwnGroup_ForItsTypeOnly()
    {
        var configuration = Configuration(
            Transport("Kafka"), ("WorkflowKafkaConsumer:TopicName", "geoassets.events"), ("WorkflowKafkaConsumer:GroupId", "geoassets.server"));
        var services = BrokerServices();

        services.AddWorkflowIntegrationConsumer<AssetMutationBatchRequestedEvent, NoopHandler>(configuration, Registration);

        services.Should().NotContain(d => d.ServiceType == typeof(InProcessIntegrationRoute));
        using var provider = services.BuildServiceProvider();
        var consumer = provider.GetServices<IHostedService>().OfType<KafkaIntegrationEventConsumer<AssetMutationBatchRequestedEvent>>().Single();
        var options = OptionsOf<KafkaConsumerOptions>(consumer);
        options.GroupId.Should().Be($"geoassets.server.{Key}", "every named consumer of a topic reads it in a group of its own");
        options.ConsumerName.Should().Be(Name);
        options.TopicName.Should().Be("geoassets.events");
        options.MessageTypes.Should().Equal(Type);
        options.TargetCatalogInstanceId.Should().Be("kernel-catalog");
    }

    [Fact]
    public void AddWorkflowIntegrationConsumer_KafkaWithAnExplicitGroupForTheConsumer_KeepsIt()
    {
        var configuration = Configuration(
            Transport("Kafka"), ("WorkflowKafkaConsumer:TopicName", "geoassets.events"),
            ($"WorkflowKafkaConsumer:Consumers:{Key}:GroupId", "my-own-group"));
        var services = BrokerServices();

        services.AddWorkflowIntegrationConsumer<AssetMutationBatchRequestedEvent, NoopHandler>(configuration, Registration);

        using var provider = services.BuildServiceProvider();
        OptionsOf<KafkaConsumerOptions>(provider.GetServices<IHostedService>().OfType<KafkaIntegrationEventConsumer<AssetMutationBatchRequestedEvent>>().Single())
            .GroupId.Should().Be("my-own-group");
    }

    [Fact]
    public void AddWorkflowIntegrationConsumer_KafkaWithoutATopic_RegistersNothing()
    {
        var services = BrokerServices();
        var before = services.Count;

        services.AddWorkflowIntegrationConsumer<AssetMutationBatchRequestedEvent, NoopHandler>(Configuration(Transport("Kafka")), Registration);

        services.Count.Should().Be(before);
    }

    [Fact]
    public async Task AddWorkflowIntegrationConsumer_ServiceBusWithATopicAndASubscription_RegistersOneNamedConsumer_ForItsTypeOnly()
    {
        var configuration = Configuration(
            Transport("ServiceBus"), ("WorkflowServiceBusConsumer:TopicName", "geoassets.events"),
            ($"WorkflowServiceBusConsumer:Consumers:{Key}:SubscriptionName", "kernel-apply"));
        var services = BrokerServices();

        services.AddWorkflowIntegrationConsumer<AssetMutationBatchRequestedEvent, NoopHandler>(configuration, Registration);

        services.Should().NotContain(d => d.ServiceType == typeof(InProcessIntegrationRoute));
        await using var provider = services.BuildServiceProvider();
        var consumer = provider.GetServices<IHostedService>().OfType<ServiceBusIntegrationEventConsumer<AssetMutationBatchRequestedEvent>>().Single();
        var options = OptionsOf<ServiceBusConsumerOptions>(consumer);
        options.SubscriptionName.Should().Be("kernel-apply");
        options.ConsumerName.Should().Be(Name);
        options.MessageTypes.Should().Equal(Type);
        options.TargetCatalogInstanceId.Should().Be("kernel-catalog");
    }

    [Fact]
    public void TransportFor_ServiceBusWithATopicButNoSubscription_FailsClearly_NamingTheMissingKey()
    {
        var configuration = Configuration(Transport("ServiceBus"), ("WorkflowServiceBusConsumer:TopicName", "geoassets.events"));

        var act = () => IntegrationConsumerHostExtensions.TransportFor(configuration, Registration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*WorkflowServiceBusConsumer:Consumers:{Key}:SubscriptionName*");
    }

    [Fact]
    public void AddWorkflowIntegrationConsumer_NullArguments_Throw()
    {
        var services = new ServiceCollection();

        ((Action)(() => IntegrationConsumerHostExtensions.AddWorkflowIntegrationConsumer<AssetMutationBatchRequestedEvent, NoopHandler>(null!, Configuration(), Registration))).Should().Throw<ArgumentNullException>();
        ((Action)(() => services.AddWorkflowIntegrationConsumer<AssetMutationBatchRequestedEvent, NoopHandler>(null!, Registration))).Should().Throw<ArgumentNullException>();
        ((Action)(() => services.AddWorkflowIntegrationConsumer<AssetMutationBatchRequestedEvent, NoopHandler>(Configuration(), null!))).Should().Throw<ArgumentNullException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ServiceCollection BrokerServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero)));
        services.AddSingleton(new GeoAssetsActivitySource("1.0.0"));
        services.AddWorkflowProtobufMessaging();
        return services;
    }

    private static T OptionsOf<T>(object consumer)
        where T : class =>
        (T)consumer.GetType().GetField("_options", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(consumer)!;

    private static IntegrationEnvelope Envelope(string messageType) => IntegrationEnvelope.Create(
        messageType: messageType,
        schemaVersion: IntegrationEventTypes.CurrentSchemaVersion,
        partitionKey: "ord-1",
        contentType: new ProtobufIntegrationMessageSerializer().ContentType,
        payload: new ProtobufIntegrationMessageSerializer().Serialize(AssetMutationBatchRequestedEvent.Create(
            "kernel-catalog", Guid.NewGuid(), "tests", "approver-1", [], TimeProvider.System, batchId: Guid.NewGuid())),
        timeProvider: TimeProvider.System,
        catalogInstanceId: "kernel-catalog");

    private static (InProcessIntegrationEventPublisher Publisher, RecordingIdempotentInbox Inbox, RecordingIntegrationEventHandler<AssetMutationBatchRequestedEvent> Handler) InProcessHarness()
    {
        var inbox = new RecordingIdempotentInbox();
        var handler = new RecordingIntegrationEventHandler<AssetMutationBatchRequestedEvent>();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(new GeoAssetsActivitySource("1.0.0"));
        services.AddWorkflowProtobufMessaging();
        services.AddSingleton<IIdempotentInbox>(inbox);
        services.AddSingleton<IIntegrationEventHandler<AssetMutationBatchRequestedEvent>>(handler);
        services.AddSingleton<InProcessIntegrationRoute>(new InProcessIntegrationRoute<AssetMutationBatchRequestedEvent>(Type, "consumer-a"));
        services.AddSingleton<InProcessIntegrationRoute>(new InProcessIntegrationRoute<AssetMutationBatchRequestedEvent>(Type, "consumer-b"));
        var provider = services.BuildServiceProvider();
        return (new InProcessIntegrationEventPublisher(provider.GetServices<InProcessIntegrationRoute>(), provider), inbox, handler);
    }

    private sealed class NoopHandler : IIntegrationEventHandler<AssetMutationBatchRequestedEvent>
    {
        public Task HandleAsync(AssetMutationBatchRequestedEvent message, IntegrationMessageContext context, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class AppliedHandler : IIntegrationEventHandler<AssetBatchAppliedEvent>
    {
        public Task HandleAsync(AssetBatchAppliedEvent message, IntegrationMessageContext context, CancellationToken ct = default) => Task.CompletedTask;
    }
}
