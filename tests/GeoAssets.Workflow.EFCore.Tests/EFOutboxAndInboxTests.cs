using System.Diagnostics;
using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Outbox;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

public sealed class EFOutboxAndInboxTests : OutboxStoreContractTests
{
    private static readonly DateTimeOffset FixedStartUtc = new(2026, 3, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly ActivitySource TestActivitySource = new("GeoAssets.Workflow.EFCore.Tests.Outbox");

    static EFOutboxAndInboxTests()
    {
        Activity.DefaultIdFormat = ActivityIdFormat.W3C;
        Activity.ForceDefaultIdFormat = true;
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        });
    }

    protected override OutboxContractHarness CreateHarness()
    {
        var fixture = new SqliteFixture();
        var timeProvider = new FakeTimeProvider(FixedStartUtc);
        var serializer = new ProtobufIntegrationMessageSerializer();
        var outbox = new EFOutboxStore(fixture.Context, timeProvider, serializer, "efcore-contract-tests");
        var inbox = new EFIdempotentInbox(fixture.Context, timeProvider);

        return new OutboxContractHarness(
            Writer: outbox,
            DispatcherStore: outbox,
            Inbox: inbox,
            TimeProvider: timeProvider,
            Scope: new SqliteFixtureDisposableAdapter(fixture));
    }

    [Fact]
    public async Task SingleDatabaseTransaction_Commit_AtomicallyPersistsServiceOrderAndOutboxMessage()
    {
        using var fixture = new SqliteFixture();
        var timeProvider = new FakeTimeProvider(FixedStartUtc);
        var serializer = new ProtobufIntegrationMessageSerializer();
        var orderRepo = new EFServiceOrderRepository(fixture.Context);
        var outbox = new EFOutboxStore(fixture.Context, timeProvider, serializer, "workflow-service");

        await using (var tx = await fixture.Context.Database.BeginTransactionAsync())
        {
            var order = new ServiceOrder
            {
                Id = "ord-atomic-commit",
                Title = "Replace junction",
                OrderTypeId = "maintenance",
                Status = ServiceOrderStatus.Pending,
                CreatedBy = "operator-1",
                CreatedAt = FixedStartUtc.UtcDateTime
            };
            await orderRepo.AddAsync(order);

            var mutatedEvent = CreateSampleMutatedEvent("catalog-main");
            await outbox.EnqueueAsync(
                message: mutatedEvent,
                messageType: IntegrationEventTypes.FeatureCatalogMutatedV1,
                partitionKey: "catalog-main:feat-100",
                catalogInstanceId: "catalog-main",
                correlationId: "corr-atomic-1");

            await tx.CommitAsync();
        }

        using var verifyContext = fixture.NewContext();
        var verifyOrderRepo = new EFServiceOrderRepository(verifyContext);
        var verifyOutbox = new EFOutboxStore(verifyContext, timeProvider, serializer);

        var loadedOrder = await verifyOrderRepo.GetByIdAsync("ord-atomic-commit");
        loadedOrder.Should().NotBeNull();
        loadedOrder!.Title.Should().Be("Replace junction");

        var claimed = await verifyOutbox.ClaimPendingBatchAsync(10, TimeSpan.FromMinutes(1), "verifier-worker");
        var pending = claimed.Should().ContainSingle().Subject;
        pending.Envelope.CatalogInstanceId.Should().Be("catalog-main");
        pending.Envelope.CorrelationId.Should().Be("corr-atomic-1");

        var roundTripped = serializer.Deserialize<FeatureCatalogMutatedEvent>(pending.Envelope.Payload);
        roundTripped.FeatureId.Should().Be("feat-100");
        roundTripped.CatalogInstanceId.Should().Be("catalog-main");
    }

    [Fact]
    public async Task SingleDatabaseTransaction_Rollback_DiscardsBothServiceOrderAndOutboxMessage()
    {
        using var fixture = new SqliteFixture();
        var timeProvider = new FakeTimeProvider(FixedStartUtc);
        var serializer = new ProtobufIntegrationMessageSerializer();
        var orderRepo = new EFServiceOrderRepository(fixture.Context);
        var outbox = new EFOutboxStore(fixture.Context, timeProvider, serializer, "workflow-service");

        await using (var tx = await fixture.Context.Database.BeginTransactionAsync())
        {
            var order = new ServiceOrder
            {
                Id = "ord-atomic-rollback",
                Title = "Should rollback",
                OrderTypeId = "maintenance",
                Status = ServiceOrderStatus.Draft,
                CreatedBy = "operator-1",
                CreatedAt = FixedStartUtc.UtcDateTime
            };
            await orderRepo.AddAsync(order);

            await outbox.EnqueueAsync(
                message: CreateSampleMutatedEvent("catalog-main"),
                messageType: IntegrationEventTypes.FeatureCatalogMutatedV1,
                partitionKey: "catalog-main:feat-100",
                catalogInstanceId: "catalog-main");

            await tx.RollbackAsync();
        }

        using var verifyContext = fixture.NewContext();
        var verifyOrderRepo = new EFServiceOrderRepository(verifyContext);
        var verifyOutbox = new EFOutboxStore(verifyContext, timeProvider, serializer);

        (await verifyOrderRepo.GetByIdAsync("ord-atomic-rollback")).Should().BeNull();
        (await verifyOutbox.ClaimPendingBatchAsync(10, TimeSpan.FromMinutes(1), "verifier-worker")).Should().BeEmpty();
    }

    [Fact]
    public async Task EnqueueAsync_WithActiveW3CActivity_CapturesTraceParentAndTraceState()
    {
        using var fixture = new SqliteFixture();
        var timeProvider = new FakeTimeProvider(FixedStartUtc);
        var serializer = new ProtobufIntegrationMessageSerializer();
        var outbox = new EFOutboxStore(fixture.Context, timeProvider, serializer, "workflow-service");

        string? expectedTraceParent;
        using (var activity = TestActivitySource.StartActivity("order.commit", ActivityKind.Internal))
        {
            activity.Should().NotBeNull();
            activity!.TraceStateString = "shard=primary";
            expectedTraceParent = activity.Id;

            await outbox.EnqueueAsync(
                message: CreateSampleMutatedEvent("catalog-trace"),
                messageType: IntegrationEventTypes.FeatureCatalogMutatedV1,
                partitionKey: "catalog-trace:feat-100",
                catalogInstanceId: "catalog-trace");
        }

        var claimed = await outbox.ClaimPendingBatchAsync(5, TimeSpan.FromMinutes(1), "trace-worker");
        var envelope = claimed.Should().ContainSingle().Subject.Envelope;

        envelope.TraceParent.Should().Be(expectedTraceParent);
        envelope.TraceState.Should().Be("shard=primary");
    }

    [Fact]
    public async Task OutboxRelayProcessor_ProcessNextBatchAsync_PublishesAndRehydratesW3CTraceContextWhenCurrentActivityIsNull()
    {
        using var fixture = new SqliteFixture();
        var timeProvider = new FakeTimeProvider(FixedStartUtc);
        var serializer = new ProtobufIntegrationMessageSerializer();
        var seedOutbox = new EFOutboxStore(fixture.Context, timeProvider, serializer, "seed-service");

        const string storedTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        const string storedSpanId = "00f067aa0ba902b7";
        const string storedTraceParent = $"00-{storedTraceId}-{storedSpanId}-01";

        var envelope = new IntegrationEnvelope(
            MessageId: Guid.Parse("77777777-1111-2222-3333-444444444444"),
            MessageType: IntegrationEventTypes.FeatureCatalogMutatedV1,
            SchemaVersion: IntegrationEventTypes.CurrentSchemaVersion,
            PartitionKey: "catalog-1:feat-100",
            ContentType: serializer.ContentType,
            Payload: serializer.Serialize(CreateSampleMutatedEvent("catalog-1")),
            OccurredAtUtc: FixedStartUtc,
            CatalogInstanceId: "catalog-1",
            SourceService: "geoassets-server",
            CorrelationId: "corr-relay-1",
            TraceParent: storedTraceParent,
            TraceState: "origin=outbox");

        await seedOutbox.EnqueueEnvelopeAsync(envelope);

        Activity.Current = null;

        var publisher = new LocalRecordingPublisher();
        using var tracer = new GeoAssetsActivitySource("1.0.0");
        await using var serviceProvider = BuildRelayScopeProvider(fixture, timeProvider, serializer);

        using var processor = new OutboxRelayProcessor(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            timeProvider,
            Options.Create(new OutboxRelayOptions()),
            tracer,
            NullLogger<OutboxRelayProcessor>.Instance);

        await processor.ProcessNextBatchAsync(CancellationToken.None);

        publisher.Published.Should().ContainSingle()
            .Which.MessageId.Should().Be(envelope.MessageId);

        var observedActivity = publisher.ObservedContexts.Should().ContainSingle().Subject;
        observedActivity.Should().NotBeNull();
        observedActivity!.Value.TraceId.ToHexString().Should().Be(storedTraceId);

        using var checkCtx = fixture.NewContext();
        var checkStore = new EFOutboxStore(checkCtx, timeProvider, serializer);
        (await checkStore.ClaimPendingBatchAsync(5, TimeSpan.FromMinutes(1), "check-worker")).Should().BeEmpty();
    }

    [Fact]
    public async Task OutboxRelayProcessor_ComputeRetryDelayAndTransientPublishFailure_SchedulesExponentialBackoffThenSucceeds()
    {
        var initial = TimeSpan.FromSeconds(2);
        var max = TimeSpan.FromSeconds(20);

        var d1 = OutboxRelayProcessor.ComputeRetryDelay(1, initial, max);
        var d2 = OutboxRelayProcessor.ComputeRetryDelay(2, initial, max);
        var d3 = OutboxRelayProcessor.ComputeRetryDelay(3, initial, max);
        var d10 = OutboxRelayProcessor.ComputeRetryDelay(10, initial, max);

        d1.Should().BeGreaterThanOrEqualTo(initial);
        d2.Should().BeGreaterThan(d1);
        d3.Should().BeGreaterThan(d2);
        d10.Should().Be(max);

        using var fixture = new SqliteFixture();
        var timeProvider = new FakeTimeProvider(FixedStartUtc);
        var serializer = new ProtobufIntegrationMessageSerializer();
        var seedStore = new EFOutboxStore(fixture.Context, timeProvider, serializer, "seed-service");

        var envelope = new IntegrationEnvelope(
            MessageId: Guid.Parse("88888888-1111-2222-3333-444444444444"),
            MessageType: IntegrationEventTypes.FeatureCatalogMutatedV1,
            SchemaVersion: 1,
            PartitionKey: "catalog-1:feat-100",
            ContentType: serializer.ContentType,
            Payload: serializer.Serialize(CreateSampleMutatedEvent("catalog-1")),
            OccurredAtUtc: FixedStartUtc,
            CatalogInstanceId: "catalog-1");
        await seedStore.EnqueueEnvelopeAsync(envelope);

        var shouldFail = true;
        var publisher = new LocalRecordingPublisher
        {
            OnPublish = _ =>
            {
                if (shouldFail)
                {
                    throw new InvalidOperationException("Simulated transient broker outage");
                }
            }
        };

        using var tracer = new GeoAssetsActivitySource("1.0.0");
        await using var serviceProvider = BuildRelayScopeProvider(fixture, timeProvider, serializer);

        using var processor = new OutboxRelayProcessor(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            timeProvider,
            Options.Create(new OutboxRelayOptions()),
            tracer,
            NullLogger<OutboxRelayProcessor>.Instance);

        await processor.ProcessNextBatchAsync(CancellationToken.None);
        publisher.Published.Should().BeEmpty();

        // Advance virtual clock past retry backoff and allow second attempt to succeed.
        shouldFail = false;
        timeProvider.Advance(TimeSpan.FromMinutes(10));

        await processor.ProcessNextBatchAsync(CancellationToken.None);
        publisher.Published.Should().ContainSingle(e => e.MessageId == envelope.MessageId);
    }

    [Fact]
    public async Task OutboxRelayProcessor_BackgroundExecutionWithFakeTimeProvider_PollsAndStopsCleanlyOnCancellation()
    {
        using var fixture = new SqliteFixture();
        var timeProvider = new FakeTimeProvider(FixedStartUtc);
        var serializer = new ProtobufIntegrationMessageSerializer();
        var seedStore = new EFOutboxStore(fixture.Context, timeProvider, serializer, "seed-service");

        var dispatchedSignal = new TaskCompletionSource<Guid>(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new LocalRecordingPublisher
        {
            OnPublish = env => dispatchedSignal.TrySetResult(env.MessageId)
        };

        var envelope = new IntegrationEnvelope(
            MessageId: Guid.Parse("66666666-1111-2222-3333-444444444444"),
            MessageType: IntegrationEventTypes.FeatureCatalogMutatedV1,
            SchemaVersion: 1,
            PartitionKey: "catalog-bg:feat-100",
            ContentType: serializer.ContentType,
            Payload: serializer.Serialize(CreateSampleMutatedEvent("catalog-bg")),
            OccurredAtUtc: FixedStartUtc,
            CatalogInstanceId: "catalog-bg");
        await seedStore.EnqueueEnvelopeAsync(envelope);

        using var tracer = new GeoAssetsActivitySource("1.0.0");
        await using var serviceProvider = BuildRelayScopeProvider(fixture, timeProvider, serializer);

        using var processor = new OutboxRelayProcessor(
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            timeProvider,
            Options.Create(new OutboxRelayOptions()),
            tracer,
            NullLogger<OutboxRelayProcessor>.Instance);

        await processor.StartAsync(CancellationToken.None);

        for (var step = 0; step < 20 && !dispatchedSignal.Task.IsCompleted; step++)
        {
            await Task.Yield();
            timeProvider.Advance(TimeSpan.FromSeconds(5));
        }

        var completed = await Task.WhenAny(dispatchedSignal.Task, Task.Delay(TimeSpan.FromSeconds(3)));
        completed.Should().BeSameAs(dispatchedSignal.Task);
        (await dispatchedSignal.Task).Should().Be(envelope.MessageId);

        await processor.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task AddWorkflowOutbox_RegistersScopedOutboxWriterDispatcherStoreAndIdempotentInbox()
    {
        using var fixture = new SqliteFixture();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(FixedStartUtc));
        services.AddSingleton(_ => new GeoAssetsActivitySource("1.0.0"));
        services.AddSingleton(_ => new GeoAssetsMeter("1.0.0"));
        services.AddWorkflowProtobufMessaging();
        services.AddSingleton<IIntegrationEventPublisher, LocalRecordingPublisher>();
        services.AddScoped<ServiceOrderDbContext>(_ => fixture.NewContext());
        services.AddWorkflowOutbox();

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        scope.ServiceProvider.GetRequiredService<IOutboxWriter>().Should().BeOfType<EFOutboxStore>();
        scope.ServiceProvider.GetRequiredService<IOutboxDispatcherStore>().Should().BeOfType<EFOutboxStore>();
        scope.ServiceProvider.GetRequiredService<IIdempotentInbox>().Should().BeOfType<EFIdempotentInbox>();
    }

    private static ServiceProvider BuildRelayScopeProvider(
        SqliteFixture fixture,
        TimeProvider timeProvider,
        IIntegrationMessageSerializer serializer)
    {
        var services = new ServiceCollection();
        services.AddScoped<IOutboxDispatcherStore>(_ =>
            new EFOutboxStore(fixture.NewContext(), timeProvider, serializer));
        return services.BuildServiceProvider();
    }

    private static FeatureCatalogMutatedEvent CreateSampleMutatedEvent(string catalogInstanceId) =>
        new(
            CatalogInstanceId: catalogInstanceId,
            OrganizationId: Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            ScopeId: "sector-1",
            FeatureId: "feat-100",
            MutationKind: AssetMutationKind.UpsertFeature,
            Name: "Node 100",
            AssetTypeId: "network-node",
            LayerId: "layer-nodes",
            Geometry: new AssetGeometrySnapshot(WireGeometryKind.Point, 4326, [-38.5014, -12.9714], [], []),
            CustomAttributes: new Dictionary<string, string>(StringComparer.Ordinal) { ["tier"] = "primary" },
            AffectedBounds: new Rfc7946BoundingBox(-38.51, -12.98, -38.50, -12.97),
            RowVersion: [0x01, 0x02, 0x03, 0x04],
            MutatedAtUtc: FixedStartUtc,
            CorrelationId: "corr-sample");

    private sealed class SqliteFixtureDisposableAdapter(SqliteFixture fixture) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            fixture.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class LocalRecordingPublisher : IIntegrationEventPublisher
    {
        public List<IntegrationEnvelope> Published { get; } = [];

        public List<ActivityContext?> ObservedContexts { get; } = [];

        public Action<IntegrationEnvelope>? OnPublish { get; init; }

        public Task PublishAsync(IntegrationEnvelope envelope, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(envelope);
            ct.ThrowIfCancellationRequested();

            ObservedContexts.Add(Activity.Current?.Context);
            OnPublish?.Invoke(envelope);
            Published.Add(envelope);
            return Task.CompletedTask;
        }

        public async Task PublishBatchAsync(IReadOnlyList<IntegrationEnvelope> envelopes, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(envelopes);
            foreach (var env in envelopes)
            {
                await PublishAsync(env, ct);
            }
        }
    }
}

