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

namespace GeoAssets.Workflow.Messaging.Kafka.Tests;

/// <summary>
/// The message-type filter of a named Kafka consumer (XD01-383): several consumers read one topic, each in its own group and for
/// its own types, so a message of another type is skipped — no handler, no inbox read, and no exception, so the consume loop
/// commits its offset and moves on — and an empty filter handles every type.
/// </summary>
public sealed class KafkaMessageTypeFilterTests
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
        var consumer = Consumer(provider);

        var processed = await consumer.ProcessEnvelopeAsync(Envelope(Other));

        processed.Should().BeFalse("nothing was handled");
        recording.Invocations.Should().BeEmpty();
        inbox.HasProcessedCalls.Should().BeEmpty("the skip comes before any inbox read");
        inbox.RecordedEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessEnvelopeAsync_AMessageOfTheHandledType_IsHandled_AndRecordedInTheInbox()
    {
        var recording = new RecordingIntegrationEventHandler<AssetMutationBatchRequestedEvent>();
        var inbox = new RecordingIdempotentInbox();
        await using var provider = Build(recording, inbox, options => options.MessageTypes.Add(Handled));

        var processed = await Consumer(provider).ProcessEnvelopeAsync(Envelope(Handled));

        processed.Should().BeTrue();
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

        first.Should().BeTrue();
        second.Should().BeTrue("an empty filter skips nothing");
        recording.Invocations.Should().HaveCount(2);
    }

    [Fact]
    public async Task ProcessEnvelopeAsync_TheFilterIsOrdinal_ADifferentlyCasedTypeIsAnotherType()
    {
        var recording = new RecordingIntegrationEventHandler<AssetMutationBatchRequestedEvent>();
        await using var provider = Build(recording, new RecordingIdempotentInbox(), options => options.MessageTypes.Add(Handled));

        var processed = await Consumer(provider).ProcessEnvelopeAsync(Envelope(Handled.ToUpperInvariant()));

        processed.Should().BeFalse();
        recording.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task ProcessEnvelopeAsync_TheFilterRunsBeforeTheShardFence_SoASkippedTypeOfAnotherCatalogIsSkippedToo()
    {
        var recording = new RecordingIntegrationEventHandler<AssetMutationBatchRequestedEvent>();
        await using var provider = Build(recording, new RecordingIdempotentInbox(), options =>
        {
            options.MessageTypes.Add(Handled);
            options.TargetCatalogInstanceId = "kernel-catalog";
        });

        var processed = await Consumer(provider).ProcessEnvelopeAsync(Envelope(Other, catalog: "another-catalog"));

        processed.Should().BeFalse();
        recording.Invocations.Should().BeEmpty();
    }

    [Fact]
    public void BindNamedConsumerOptions_LayersTheConsumersSectionOverTheSharedSettings_ThenTheCallback()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["WorkflowKafkaConsumer:TopicName"] = "shared-topic",
            ["WorkflowKafkaConsumer:GroupId"] = "shared-group",
            ["WorkflowKafkaConsumer:Consumers:Saga:GroupId"] = "saga-group",
        }).Build();

        var options = WorkflowKafkaServiceExtensions.BindNamedConsumerOptions(configuration, "Saga", o => o.ConsumerName = "from-callback");

        options.TopicName.Should().Be("shared-topic");
        options.GroupId.Should().Be("saga-group");
        options.ConsumerName.Should().Be("from-callback");
        WorkflowKafkaServiceExtensions.BindNamedConsumerOptions(configuration, "Another").GroupId.Should().Be("shared-group");
        ((Action)(() => WorkflowKafkaServiceExtensions.BindNamedConsumerOptions(null!, "Saga"))).Should().Throw<ArgumentNullException>();
        ((Action)(() => WorkflowKafkaServiceExtensions.BindNamedConsumerOptions(configuration, " "))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void KafkaConsumerOptions_MessageTypes_IsEmptyByDefault()
    {
        new KafkaConsumerOptions().MessageTypes.Should().BeEmpty();
        KafkaConsumerOptions.ConsumersSectionName.Should().Be("Consumers");
    }

    private ServiceProvider Build(
        RecordingIntegrationEventHandler<AssetMutationBatchRequestedEvent> handler,
        IIdempotentInbox inbox,
        Action<KafkaConsumerOptions> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(FixedUtc));
        services.AddSingleton(new GeoAssetsActivitySource("1.0.0"));
        services.AddSingleton<IIntegrationMessageSerializer>(_serializer);
        services.AddScoped(_ => inbox);
        services.AddSingleton<IIntegrationEventHandler<AssetMutationBatchRequestedEvent>>(handler);
        services.AddKafkaIntegrationConsumer<AssetMutationBatchRequestedEvent, PassThroughHandler>(
            new ConfigurationBuilder().Build(), "Saga", configure);
        services.AddSingleton<IIntegrationEventHandler<AssetMutationBatchRequestedEvent>>(handler);
        return services.BuildServiceProvider();
    }

    private static KafkaIntegrationEventConsumer<AssetMutationBatchRequestedEvent> Consumer(IServiceProvider provider) =>
        provider.GetServices<IHostedService>().OfType<KafkaIntegrationEventConsumer<AssetMutationBatchRequestedEvent>>().Single();

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
