using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using Xunit;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Liskov Substitution Principle behavioral contract suite for <see cref="IOutboxWriter"/>,
/// <see cref="IOutboxDispatcherStore"/>, and <see cref="IIdempotentInbox"/> implementations.
/// </summary>
public abstract class OutboxStoreContractTests
{
    protected abstract OutboxContractHarness CreateHarness();

    [Fact]
    public async Task ClaimPendingBatchAsync_ClaimsMessagesInChronologicalOrder_UpToBatchSize()
    {
        await using var harness = CreateHarness();

        var firstEnvelope = IntegrationMessagingTestDataBuilders.Envelope()
            .WithMessageId(Guid.Parse("00000000-0000-0000-0000-000000000001"))
            .WithOccurredAtUtc(harness.TimeProvider.GetUtcNow())
            .Build();
        await harness.Writer.EnqueueEnvelopeAsync(firstEnvelope);

        harness.TimeProvider.Advance(TimeSpan.FromSeconds(5));
        var secondEnvelope = IntegrationMessagingTestDataBuilders.Envelope()
            .WithMessageId(Guid.Parse("00000000-0000-0000-0000-000000000002"))
            .WithOccurredAtUtc(harness.TimeProvider.GetUtcNow())
            .Build();
        await harness.Writer.EnqueueEnvelopeAsync(secondEnvelope);

        harness.TimeProvider.Advance(TimeSpan.FromSeconds(5));
        var thirdEnvelope = IntegrationMessagingTestDataBuilders.Envelope()
            .WithMessageId(Guid.Parse("00000000-0000-0000-0000-000000000003"))
            .WithOccurredAtUtc(harness.TimeProvider.GetUtcNow())
            .Build();
        await harness.Writer.EnqueueEnvelopeAsync(thirdEnvelope);

        var claimed = await harness.DispatcherStore.ClaimPendingBatchAsync(
            batchSize: 2,
            leaseDuration: TimeSpan.FromMinutes(1),
            workerId: "worker-1");

        claimed.Should().HaveCount(2);
        claimed.Select(m => m.Envelope.MessageId).Should().Equal(
            firstEnvelope.MessageId,
            secondEnvelope.MessageId);
        claimed.Should().AllSatisfy(m => m.LockedUntilUtc.Should().NotBeNull());
    }

    [Fact]
    public async Task EnqueueEnvelopeAsync_RoundTripsFullEnvelopeMetadataAndPayload()
    {
        await using var harness = CreateHarness();
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["x-region"] = "northeast",
            ["x-shard"] = "01"
        };
        var payloadBytes = new byte[] { 0x10, 0x20, 0x30, 0x40, 0x50 };
        var expected = IntegrationMessagingTestDataBuilders.Envelope()
            .WithMessageId(Guid.Parse("12345678-1234-1234-1234-1234567890ab"))
            .WithMessageType(IntegrationEventTypes.AssetMutationBatchRequestedV1)
            .WithSchemaVersion(IntegrationEventTypes.CurrentSchemaVersion)
            .WithPartitionKey("catalog-a:batch-1")
            .WithContentType("application/x-protobuf")
            .WithPayload(payloadBytes)
            .WithOccurredAtUtc(harness.TimeProvider.GetUtcNow())
            .WithCatalogInstanceId("catalog-a")
            .WithSourceService("order-service")
            .WithCorrelationId("corr-xyz")
            .WithCausationId("caus-abc")
            .WithTraceParent("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01")
            .WithTraceState("vendor=opaque")
            .WithHeaders(headers)
            .Build();

        await harness.Writer.EnqueueEnvelopeAsync(expected);

        var claimed = await harness.DispatcherStore.ClaimPendingBatchAsync(
            batchSize: 10,
            leaseDuration: TimeSpan.FromMinutes(2),
            workerId: "worker-1");

        var actual = claimed.Should().ContainSingle().Subject.Envelope;
        actual.MessageId.Should().Be(expected.MessageId);
        actual.MessageType.Should().Be(expected.MessageType);
        actual.SchemaVersion.Should().Be(expected.SchemaVersion);
        actual.PartitionKey.Should().Be(expected.PartitionKey);
        actual.ContentType.Should().Be(expected.ContentType);
        actual.Payload.ToArray().Should().Equal(payloadBytes);
        actual.OccurredAtUtc.Should().Be(expected.OccurredAtUtc);
        actual.CatalogInstanceId.Should().Be("catalog-a");
        actual.SourceService.Should().Be("order-service");
        actual.CorrelationId.Should().Be("corr-xyz");
        actual.CausationId.Should().Be("caus-abc");
        actual.TraceParent.Should().Be(expected.TraceParent);
        actual.TraceState.Should().Be(expected.TraceState);
        actual.Headers.Should().NotBeNull();
        actual.Headers.Should().Contain("x-region", "northeast");
        actual.Headers.Should().Contain("x-shard", "01");
    }

    [Fact]
    public async Task EnqueueAsync_TypedEvent_AssignsUniqueMessageIdAndPersistsCatalogInstanceId()
    {
        await using var harness = CreateHarness();
        var domainEvent = IntegrationMessagingTestDataBuilders.FeatureCatalogMutated()
            .WithCatalogInstanceId("catalog-east")
            .Build();

        var messageId = await harness.Writer.EnqueueAsync(
            message: domainEvent,
            messageType: IntegrationEventTypes.FeatureCatalogMutatedV1,
            partitionKey: "catalog-east:feat-100",
            catalogInstanceId: "catalog-east",
            correlationId: "corr-typed-1",
            causationId: "caus-typed-1");

        messageId.Should().NotBeEmpty();

        var claimed = await harness.DispatcherStore.ClaimPendingBatchAsync(
            batchSize: 5,
            leaseDuration: TimeSpan.FromMinutes(1),
            workerId: "worker-1");

        var single = claimed.Should().ContainSingle().Subject;
        single.Envelope.MessageId.Should().Be(messageId);
        single.Envelope.CatalogInstanceId.Should().Be("catalog-east");
        single.Envelope.CorrelationId.Should().Be("corr-typed-1");
        single.Envelope.CausationId.Should().Be("caus-typed-1");
        single.Envelope.Payload.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ClaimPendingBatchAsync_ExcludesActivelyLeasedMessages_UntilLeaseReleased()
    {
        await using var harness = CreateHarness();
        var envelope = IntegrationMessagingTestDataBuilders.Envelope()
            .WithOccurredAtUtc(harness.TimeProvider.GetUtcNow())
            .Build();
        await harness.Writer.EnqueueEnvelopeAsync(envelope);

        var firstClaim = await harness.DispatcherStore.ClaimPendingBatchAsync(
            batchSize: 5,
            leaseDuration: TimeSpan.FromMinutes(5),
            workerId: "worker-a");
        firstClaim.Should().ContainSingle();

        harness.TimeProvider.Advance(TimeSpan.FromMinutes(2));
        var secondClaimWhileLeased = await harness.DispatcherStore.ClaimPendingBatchAsync(
            batchSize: 5,
            leaseDuration: TimeSpan.FromMinutes(5),
            workerId: "worker-b");
        secondClaimWhileLeased.Should().BeEmpty();

        harness.TimeProvider.Advance(TimeSpan.FromMinutes(4));
        var releasedCount = await harness.DispatcherStore.ReleaseExpiredLeasesAsync();
        releasedCount.Should().Be(1);

        var reclaimed = await harness.DispatcherStore.ClaimPendingBatchAsync(
            batchSize: 5,
            leaseDuration: TimeSpan.FromMinutes(5),
            workerId: "worker-b");
        reclaimed.Should().ContainSingle(m => m.Envelope.MessageId == envelope.MessageId);
    }

    [Fact]
    public async Task MarkDispatchedAsync_FencesStaleWorker_AndPreventsReclaimWhenOwnerCompletes()
    {
        await using var harness = CreateHarness();
        var envelope = IntegrationMessagingTestDataBuilders.Envelope()
            .WithOccurredAtUtc(harness.TimeProvider.GetUtcNow())
            .Build();
        await harness.Writer.EnqueueEnvelopeAsync(envelope);

        await harness.DispatcherStore.ClaimPendingBatchAsync(
            batchSize: 1,
            leaseDuration: TimeSpan.FromMinutes(2),
            workerId: "worker-owner");

        try
        {
            await harness.DispatcherStore.MarkDispatchedAsync(envelope.MessageId, "worker-intruder");
        }
        catch (InvalidOperationException)
        {
            // Explicit fencing exception is an acceptable realization alongside 0-row fenced no-op.
        }

        harness.TimeProvider.Advance(TimeSpan.FromMinutes(3));
        await harness.DispatcherStore.ReleaseExpiredLeasesAsync();

        var reclaimedAfterIntruder = await harness.DispatcherStore.ClaimPendingBatchAsync(
            batchSize: 1,
            leaseDuration: TimeSpan.FromMinutes(2),
            workerId: "worker-legit");
        reclaimedAfterIntruder.Should().ContainSingle(m => m.Envelope.MessageId == envelope.MessageId);

        await harness.DispatcherStore.MarkDispatchedAsync(envelope.MessageId, "worker-legit");

        harness.TimeProvider.Advance(TimeSpan.FromMinutes(10));
        await harness.DispatcherStore.ReleaseExpiredLeasesAsync();
        var afterDispatched = await harness.DispatcherStore.ClaimPendingBatchAsync(
            batchSize: 5,
            leaseDuration: TimeSpan.FromMinutes(2),
            workerId: "worker-any");
        afterDispatched.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkFailedAsync_SchedulesRetryBeforeMaxAttempts_AndTransitionsTerminalFailedAtMaxAttempts()
    {
        await using var harness = CreateHarness();
        var envelope = IntegrationMessagingTestDataBuilders.Envelope()
            .WithOccurredAtUtc(harness.TimeProvider.GetUtcNow())
            .Build();
        await harness.Writer.EnqueueEnvelopeAsync(envelope);

        var firstClaim = (await harness.DispatcherStore.ClaimPendingBatchAsync(
            batchSize: 1,
            leaseDuration: TimeSpan.FromMinutes(1),
            workerId: "worker-1")).Should().ContainSingle().Subject;

        await harness.DispatcherStore.MarkFailedAsync(
            messageId: envelope.MessageId,
            workerId: "worker-1",
            errorMessage: "Broker unavailable (attempt 1)",
            retryDelay: TimeSpan.FromSeconds(30),
            maxAttempts: 2);

        harness.TimeProvider.Advance(TimeSpan.FromSeconds(10));
        var tooEarly = await harness.DispatcherStore.ClaimPendingBatchAsync(
            batchSize: 1,
            leaseDuration: TimeSpan.FromMinutes(1),
            workerId: "worker-1");
        tooEarly.Should().BeEmpty();

        harness.TimeProvider.Advance(TimeSpan.FromSeconds(25));
        var secondClaim = (await harness.DispatcherStore.ClaimPendingBatchAsync(
            batchSize: 1,
            leaseDuration: TimeSpan.FromMinutes(1),
            workerId: "worker-1")).Should().ContainSingle().Subject;
        secondClaim.AttemptCount.Should().BeGreaterThan(firstClaim.AttemptCount);

        await harness.DispatcherStore.MarkFailedAsync(
            messageId: envelope.MessageId,
            workerId: "worker-1",
            errorMessage: "Broker unavailable (attempt 2)",
            retryDelay: TimeSpan.FromSeconds(30),
            maxAttempts: 2);

        harness.TimeProvider.Advance(TimeSpan.FromHours(1));
        await harness.DispatcherStore.ReleaseExpiredLeasesAsync();

        var afterMaxAttempts = await harness.DispatcherStore.ClaimPendingBatchAsync(
            batchSize: 5,
            leaseDuration: TimeSpan.FromMinutes(1),
            workerId: "worker-1");
        afterMaxAttempts.Should().BeEmpty();
    }

    [Fact]
    public async Task IdempotentInbox_DeduplicatesCompositeMessageIdAndConsumerName()
    {
        await using var harness = CreateHarness();
        var messageId = Guid.Parse("99999999-8888-7777-6666-555555555555");

        (await harness.Inbox.HasProcessedAsync(messageId, "consumer-alpha")).Should().BeFalse();

        var firstAlpha = await harness.Inbox.TryRecordProcessedAsync(
            messageId,
            "consumer-alpha",
            IntegrationEventTypes.FeatureCatalogMutatedV1,
            TimeSpan.FromDays(7));
        firstAlpha.Should().BeTrue();

        (await harness.Inbox.HasProcessedAsync(messageId, "consumer-alpha")).Should().BeTrue();

        var duplicateAlpha = await harness.Inbox.TryRecordProcessedAsync(
            messageId,
            "consumer-alpha",
            IntegrationEventTypes.FeatureCatalogMutatedV1,
            TimeSpan.FromDays(7));
        duplicateAlpha.Should().BeFalse();

        (await harness.Inbox.HasProcessedAsync(messageId, "consumer-beta")).Should().BeFalse();

        var firstBeta = await harness.Inbox.TryRecordProcessedAsync(
            messageId,
            "consumer-beta",
            IntegrationEventTypes.FeatureCatalogMutatedV1,
            TimeSpan.FromDays(7));
        firstBeta.Should().BeTrue();
    }

    [Fact]
    public async Task PurgeExpiredAsync_DeletesExpiredEntriesInBoundedBatches_PreservingActiveEntries()
    {
        await using var harness = CreateHarness();
        var expiredId1 = Guid.Parse("10000000-0000-0000-0000-000000000001");
        var expiredId2 = Guid.Parse("10000000-0000-0000-0000-000000000002");
        var expiredId3 = Guid.Parse("10000000-0000-0000-0000-000000000003");
        var activeId = Guid.Parse("10000000-0000-0000-0000-000000000099");

        await harness.Inbox.TryRecordProcessedAsync(expiredId1, "consumer-a", IntegrationEventTypes.OrderStateChangedV1, TimeSpan.FromHours(1));
        await harness.Inbox.TryRecordProcessedAsync(expiredId2, "consumer-a", IntegrationEventTypes.OrderStateChangedV1, TimeSpan.FromHours(1));
        await harness.Inbox.TryRecordProcessedAsync(expiredId3, "consumer-a", IntegrationEventTypes.OrderStateChangedV1, TimeSpan.FromHours(1));
        await harness.Inbox.TryRecordProcessedAsync(activeId, "consumer-a", IntegrationEventTypes.OrderStateChangedV1, TimeSpan.FromHours(24));

        harness.TimeProvider.Advance(TimeSpan.FromHours(2));

        var firstPurge = await harness.Inbox.PurgeExpiredAsync(maxBatchSize: 2);
        firstPurge.Should().Be(2);

        var secondPurge = await harness.Inbox.PurgeExpiredAsync(maxBatchSize: 2);
        secondPurge.Should().Be(1);

        (await harness.Inbox.HasProcessedAsync(expiredId1, "consumer-a")).Should().BeFalse();
        (await harness.Inbox.HasProcessedAsync(expiredId2, "consumer-a")).Should().BeFalse();
        (await harness.Inbox.HasProcessedAsync(expiredId3, "consumer-a")).Should().BeFalse();
        (await harness.Inbox.HasProcessedAsync(activeId, "consumer-a")).Should().BeTrue();
    }

    [Fact]
    public async Task OutboxAndInbox_InvalidArguments_ThrowArgumentExceptions()
    {
        await using var harness = CreateHarness();

        var nullEnvelopeAct = () => harness.Writer.EnqueueEnvelopeAsync(null!);
        await nullEnvelopeAct.Should().ThrowAsync<ArgumentNullException>();

        var zeroBatchClaimAct = () => harness.DispatcherStore.ClaimPendingBatchAsync(0, TimeSpan.FromMinutes(1), "worker-1");
        await zeroBatchClaimAct.Should().ThrowAsync<ArgumentOutOfRangeException>();

        var blankWorkerAct = () => harness.DispatcherStore.ClaimPendingBatchAsync(5, TimeSpan.FromMinutes(1), "   ");
        await blankWorkerAct.Should().ThrowAsync<ArgumentException>();

        var blankConsumerAct = () => harness.Inbox.HasProcessedAsync(Guid.NewGuid(), "");
        await blankConsumerAct.Should().ThrowAsync<ArgumentException>();

        var zeroPurgeBatchAct = () => harness.Inbox.PurgeExpiredAsync(0);
        await zeroPurgeBatchAct.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task OutboxAndInbox_PreCancelledToken_ThrowsOperationCanceledException()
    {
        await using var harness = CreateHarness();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var envelope = IntegrationMessagingTestDataBuilders.Envelope()
            .WithOccurredAtUtc(harness.TimeProvider.GetUtcNow())
            .Build();

        var enqueueAct = () => harness.Writer.EnqueueEnvelopeAsync(envelope, cts.Token);
        await enqueueAct.Should().ThrowAsync<OperationCanceledException>();

        var claimAct = () => harness.DispatcherStore.ClaimPendingBatchAsync(5, TimeSpan.FromMinutes(1), "worker-1", cts.Token);
        await claimAct.Should().ThrowAsync<OperationCanceledException>();

        var inboxAct = () => harness.Inbox.HasProcessedAsync(envelope.MessageId, "consumer-a", cts.Token);
        await inboxAct.Should().ThrowAsync<OperationCanceledException>();
    }

}
