using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Messaging.ServiceBus.Tests;

/// <summary>
/// The message-type filter of a named Service Bus consumer (XD01-383): several consumers read one topic, each on its own
/// subscription and for its own types, so a message of another type is completed without being handled
/// (<see cref="ServiceBusConsumeDisposition.MessageTypeSkipped"/>) — no handler, no inbox read, no exception — and an empty
/// filter handles every type.
/// </summary>
public sealed class ServiceBusMessageTypeFilterTests
{
    private const string Handled = IntegrationEventTypes.AssetMutationBatchRequestedV1;
    private const string Other = IntegrationEventTypes.AssetMutationBatchAppliedV1;

    private static readonly DateTimeOffset FixedUtc = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);

    private readonly ProtobufIntegrationMessageSerializer _serializer = new();

    [Fact]
    public async Task ProcessEnvelopeAsync_AMessageOfAnotherType_IsSkipped_WithNoHandlerCallNoInboxReadAndNoException()
    {
        var recording = new RecordingIntegrationEventHandler<AssetMutationBatchRequestedEvent>();
        var inbox = new RecordingIdempotentInbox();
        await using var provider = Build(recording, inbox, options => options.MessageTypes.Add(Handled));

        var disposition = await Consumer(provider).ProcessEnvelopeAsync(Envelope(Other));

        disposition.Should().Be(ServiceBusConsumeDisposition.MessageTypeSkipped);
        recording.Invocations.Should().BeEmpty();
        inbox.HasProcessedCalls.Should().BeEmpty("the skip comes before any inbox read");
        inbox.RecordedEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessEnvelopeAsync_AMessageOfTheHandledType_IsHandled_AndCompleted()
    {
        var recording = new RecordingIntegrationEventHandler<AssetMutationBatchRequestedEvent>();
        var inbox = new RecordingIdempotentInbox();
        await using var provider = Build(recording, inbox, options => options.MessageTypes.Add(Handled));

        var disposition = await Consumer(provider).ProcessEnvelopeAsync(Envelope(Handled));

        disposition.Should().Be(ServiceBusConsumeDisposition.Complete);
        recording.Invocations.Should().ContainSingle();
        inbox.RecordedEntries.Should().ContainSingle();
    }

    [Fact]
    public async Task ProcessEnvelopeAsync_WithNoFilter_HandlesEveryType()
    {
        var recording = new RecordingIntegrationEventHandler<AssetMutationBatchRequestedEvent>();
        await using var provider = Build(recording, new RecordingIdempotentInbox(), _ => { });
        var consumer = Consumer(provider);

        var first = await consumer.ProcessEnvelopeAsync(Envelope(Handled));
        var second = await consumer.ProcessEnvelopeAsync(Envelope(Other));

        first.Should().Be(ServiceBusConsumeDisposition.Complete);
        second.Should().Be(ServiceBusConsumeDisposition.Complete, "an empty filter skips nothing");
        recording.Invocations.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessEnvelopeAsync_TheFilterIsOrdinal_ADifferentlyCasedTypeIsAnotherType()
    {
        var recording = new RecordingIntegrationEventHandler<AssetMutationBatchRequestedEvent>();
        await using var provider = Build(recording, new RecordingIdempotentInbox(), options => options.MessageTypes.Add(Handled));

        var disposition = await Consumer(provider).ProcessEnvelopeAsync(Envelope(Handled.ToUpperInvariant()));

        disposition.Should().Be(ServiceBusConsumeDisposition.MessageTypeSkipped);
        recording.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessEnvelopeAsync_TheFilterRunsBeforeTheShardFence()
    {
        var recording = new RecordingIntegrationEventHandler<AssetMutationBatchRequestedEvent>();
        await using var provider = Build(recording, new RecordingIdempotentInbox(), options =>
        {
            options.MessageTypes.Add(Handled);
            options.TargetCatalogInstanceId = "kernel-catalog";
        });

        var disposition = await Consumer(provider).ProcessEnvelopeAsync(Envelope(Other, catalog: "another-catalog"));

        disposition.Should().Be(ServiceBusConsumeDisposition.MessageTypeSkipped, "not the fenced disposition: the type was not this consumer's in the first place");
    }

    [Fact]
    public void BindNamedConsumerOptions_LayersTheConsumersSectionOverTheSharedSettings_ThenTheCallback()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["WorkflowServiceBusConsumer:TopicName"] = "shared-topic",
            ["WorkflowServiceBusConsumer:SubscriptionName"] = "shared-subscription",
            ["WorkflowServiceBusConsumer:Consumers:Saga:SubscriptionName"] = "saga-subscription",
        }).Build();

        var options = WorkflowServiceBusServiceExtensions.BindNamedConsumerOptions(configuration, "Saga", o => o.ConsumerName = "from-callback");

        options.TopicName.Should().Be("shared-topic");
        options.SubscriptionName.Should().Be("saga-subscription");
        options.ConsumerName.Should().Be("from-callback");
        WorkflowServiceBusServiceExtensions.BindNamedConsumerOptions(configuration, "Another").SubscriptionName.Should().Be("shared-subscription");
        ((Action)(() => WorkflowServiceBusServiceExtensions.BindNamedConsumerOptions(null!, "Saga"))).Should().Throw<ArgumentNullException>();
        ((Action)(() => WorkflowServiceBusServiceExtensions.BindNamedConsumerOptions(configuration, " "))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ServiceBusConsumerOptions_MessageTypes_IsEmptyByDefault()
    {
        new ServiceBusConsumerOptions().MessageTypes.Should().BeEmpty();
        ServiceBusConsumerOptions.ConsumersSectionName.Should().Be("Consumers");
        ((int)ServiceBusConsumeDisposition.MessageTypeSkipped).Should().Be(5);
    }

    private ServiceProvider Build(
        RecordingIntegrationEventHandler<AssetMutationBatchRequestedEvent> handler,
        IIdempotentInbox inbox,
        Action<ServiceBusConsumerOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(FixedUtc));
        services.AddSingleton(new GeoAssetsActivitySource("1.0.0"));
        services.AddSingleton<IIntegrationMessageSerializer>(_serializer);
        services.AddScoped(_ => inbox);
        services.AddSingleton<IIntegrationEventHandler<AssetMutationBatchRequestedEvent>>(handler);
        services.AddServiceBusIntegrationConsumer<AssetMutationBatchRequestedEvent, PassThroughHandler>(
            new ConfigurationBuilder().Build(), "Saga", configure);
        return services.BuildServiceProvider();
    }

    private static ServiceBusIntegrationEventConsumer<AssetMutationBatchRequestedEvent> Consumer(IServiceProvider provider) =>
        provider.GetServices<IHostedService>().OfType<ServiceBusIntegrationEventConsumer<AssetMutationBatchRequestedEvent>>().Single();

    private IntegrationEnvelope Envelope(string messageType, string catalog = "kernel-catalog") => new(
        MessageId: Guid.NewGuid(),
        MessageType: messageType,
        SchemaVersion: IntegrationEventTypes.CurrentSchemaVersion,
        PartitionKey: "ord-1",
        ContentType: _serializer.ContentType,
        Payload: _serializer.Serialize(AssetMutationBatchRequestedEvent.Create(
            catalog, Guid.NewGuid(), "tests", "approver-1", [], TimeProvider.System, batchId: Guid.NewGuid())),
        OccurredAtUtc: FixedUtc,
        CatalogInstanceId: catalog,
        SourceService: "tests");

    private sealed class PassThroughHandler : IIntegrationEventHandler<AssetMutationBatchRequestedEvent>
    {
        public Task HandleAsync(AssetMutationBatchRequestedEvent message, IntegrationMessageContext context, CancellationToken ct = default) => Task.CompletedTask;
    }
}
