using System.Diagnostics;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Services;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// The kernel's asset-mutation consumer (XD01-383), <see cref="AssetMutationBatchApplyHandler"/>, over test doubles of the
/// applier, the inbox, the outbox and the unit of work: what it answers, what it records, and what it refuses to turn into an
/// answer. A business refusal — a missing feature, an invalid attribute, an unsupported mutation — is a <c>Rejected</c> reply;
/// a failure that may pass (the database, a timeout, a cancellation) or a batch that is not this host's is no answer at all: it
/// propagates, and nothing is recorded or staged, so the message is delivered again.
/// </summary>
public sealed class AssetMutationBatchApplyHandlerTests : IDisposable
{
    private const string CatalogInstanceId = "kernel-catalog";

    private static readonly Guid Organization = new("0e0e0e0e-0000-4000-8000-000000000383");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 20, 9, 0, 0, TimeSpan.Zero));
    private readonly GeoAssetsActivitySource _tracer = new("1.0.0");
    private readonly RecordingIdempotentInbox _inbox = new();
    private readonly RecordingOutboxWriter _outbox = new();
    private readonly InMemoryWorkflowUnitOfWork _unitOfWork = new();
    private readonly TenantOrganizationScope _scope = new();
    private readonly ScriptedApplier _applier = new();

    public void Dispose() => _tracer.Dispose();

    private AssetMutationBatchApplyHandler Handler(ILogger<AssetMutationBatchApplyHandler>? logger = null, IWorkflowUnitOfWork? unitOfWork = null) => new(
        new KernelAssetMutationApplier(CatalogInstanceId, _applier), _inbox, _outbox, unitOfWork ?? _unitOfWork, _scope, _tracer, _clock,
        logger ?? NullLogger<AssetMutationBatchApplyHandler>.Instance);

    private AssetMutationBatchRequestedEvent Batch(string catalogInstanceId = CatalogInstanceId, string? verticalId = "neutral-operator") =>
        AssetMutationBatchRequestedEvent.Create(
            catalogInstanceId, Organization, "tests", "approver-1",
            [new AssetFeatureMutationItem("pole-1", AssetMutationKind.UpdateAttributes, string.Empty, "pole", string.Empty, string.Empty, null, [], new Dictionary<string, string>(StringComparer.Ordinal))],
            _clock, batchId: Guid.NewGuid(), sourceOrderId: "ord-1", sourceOrderTypeId: "pole-work", correlationId: "corr-1", sourceVerticalId: verticalId);

    private static IntegrationMessageContext Context(Guid? messageId = null, string? traceParent = null) =>
        new IntegrationMessageContextBuilder()
            .WithMessageId(messageId ?? Guid.NewGuid())
            .WithMessageType(IntegrationEventTypes.AssetMutationBatchRequestedV1)
            .WithTraceParent(traceParent)
            .Build();

    // ── Applied ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_AppliesTheBatch_RecordsTheInbox_AndStagesTheAppliedReply_RoutedBackToTheRequester()
    {
        var batch = Batch();
        var context = Context();

        await Handler().HandleAsync(batch, context);

        _applier.Received.Should().ContainSingle().Which.Should().BeSameAs(batch);
        var reply = _outbox.Messages.Should().ContainSingle().Subject;
        reply.MessageType.Should().Be(IntegrationEventTypes.AssetMutationBatchAppliedV1);
        reply.PartitionKey.Should().Be("ord-1");
        reply.CorrelationId.Should().Be("corr-1");
        reply.CausationId.Should().Be(context.MessageId.ToString("D"), "the reply is caused by the message it answers");
        var applied = reply.Message.Should().BeOfType<AssetBatchAppliedEvent>().Subject;
        applied.BatchId.Should().Be(batch.BatchId);
        applied.SourceVerticalId.Should().Be("neutral-operator", "the reply carries the requester's vertical so its saga recognizes it");
        applied.SourceOrderId.Should().Be("ord-1");
        applied.CorrelationId.Should().Be("corr-1");
        _inbox.RecordedEntries.Should().ContainSingle().Which.Should().Match<(Guid MessageId, string ConsumerName, string MessageType, TimeSpan RetentionPeriod)>(entry =>
            entry.MessageId == context.MessageId && entry.ConsumerName == AssetMutationBatchApplyHandler.ConsumerName
            && entry.MessageType == IntegrationEventTypes.AssetMutationBatchRequestedV1 && entry.RetentionPeriod == AssetMutationBatchApplyHandler.InboxRetention);
        _unitOfWork.Started.Should().Be(1, "the inbox record and the reply are staged in one unit");
        _scope.OrganizationId.Should().Be(Organization, "the catalog and the stores are scoped to the batch's organization");
    }

    [Fact]
    public async Task HandleAsync_AnAppliedResultRecordedBeforeTheRoutingFieldsExisted_IsRoutedAsTheBatchSays()
    {
        var batch = Batch();
        _applier.Result = batchToApply => AssetBatchAppliedEvent.Create(
            batchToApply.BatchId, batchToApply.CatalogInstanceId, batchToApply.OrganizationId, batchToApply.RequestedByService, "kernel", [], _clock);

        await Handler().HandleAsync(batch, Context());

        var applied = (AssetBatchAppliedEvent)_outbox.Messages.Single().Message;
        applied.SourceVerticalId.Should().Be("neutral-operator");
        applied.CorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task HandleAsync_ABatchWithNoSourceOrder_IsPartitionedByItsBatchId()
    {
        var batch = Batch() with { SourceOrderId = null };

        await Handler().HandleAsync(batch, Context());

        _outbox.Messages.Single().PartitionKey.Should().Be(batch.BatchId.ToString("D"));
    }

    // ── Duplicate delivery ────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_TheSameMessageTwice_AppliesOnce_AndAnswersOnce()
    {
        var batch = Batch();
        var context = Context();
        var sut = Handler();

        await sut.HandleAsync(batch, context);
        await sut.HandleAsync(batch, context);

        _applier.Received.Should().HaveCount(1, "the inbox knew the message");
        _outbox.Messages.Should().ContainSingle();
        _unitOfWork.Started.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_TheSameBatchInANewMessage_IsAppliedAgainByTheIdempotentApplier_AndAnsweredAgain()
    {
        var batch = Batch();
        var sut = Handler();
        await sut.HandleAsync(batch, Context());

        await sut.HandleAsync(batch, Context());

        _applier.Received.Should().HaveCount(2, "the applier's own store makes the second a no-op: it still has to answer");
        _outbox.Messages.Should().HaveCount(2).And.OnlyContain(m => m.MessageType == IntegrationEventTypes.AssetMutationBatchAppliedV1);
    }

    // ── Crash between apply and reply ─────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_WhenTheUnitOfWorkFailsAfterTheApply_PropagatesWithNothingRecordedOrStaged_AndARedeliveryAnswers()
    {
        var batch = Batch();
        var context = Context();
        var failing = new FailingUnitOfWork(_unitOfWork) { Failures = 1 };
        var sut = Handler(unitOfWork: failing);

        var first = () => sut.HandleAsync(batch, context);

        await first.Should().ThrowAsync<TimeoutException>();
        _applier.Received.Should().HaveCount(1, "the batch was applied");
        _outbox.Messages.Should().BeEmpty("the reply was not staged");
        _inbox.RecordedEntries.Should().BeEmpty("the inbox record was not written");

        await sut.HandleAsync(batch, context);

        _applier.Received.Should().HaveCount(2, "the redelivery reaches the applier again, which no-ops");
        _outbox.Messages.Should().ContainSingle().Which.MessageType.Should().Be(IntegrationEventTypes.AssetMutationBatchAppliedV1);
        _inbox.RecordedEntries.Should().ContainSingle();
    }

    // ── Rejected ──────────────────────────────────────────────────────────────

    public static TheoryData<Exception, string, string[]> BusinessFailures() => new()
    {
        { new AssetMutationFeatureNotFoundException("pole-9", "Feature 'pole-9' not found for UpdateAttributes."), AssetMutationBatchRejectionCodes.FeatureNotFound, ["pole-9"] },
        { new KeyNotFoundException("Layer not found."), AssetMutationBatchRejectionCodes.FeatureNotFound, [] },
        { new GeoFeatureAttributeValidationException(Guid.NewGuid(), ["plate is required"]), AssetMutationBatchRejectionCodes.ValidationFailed, [] },
        { new GeoFeatureGeometryMismatchException(Guid.NewGuid(), GeometryType.Point, GeometryType.Polygon), AssetMutationBatchRejectionCodes.ValidationFailed, [] },
        { new NotSupportedException("Mutation kind not supported."), AssetMutationBatchRejectionCodes.Unsupported, [] },
        { new MalformedBatchException("The batch is malformed."), AssetMutationBatchRejectionCodes.InvalidBatch, [] },
    };

    [Theory]
    [MemberData(nameof(BusinessFailures))]
    public async Task HandleAsync_ABusinessRefusal_IsAnsweredWithARejection_NamingTheCodeTheReasonAndTheConflictingFeatures(
        Exception failure, string expectedCode, string[] expectedConflicting)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(expectedConflicting);
        var batch = Batch();
        var context = Context();
        _applier.Failure = failure;

        await Handler().HandleAsync(batch, context);

        var reply = _outbox.Messages.Should().ContainSingle().Subject;
        reply.MessageType.Should().Be(IntegrationEventTypes.AssetMutationBatchRejectedV1);
        reply.PartitionKey.Should().Be("ord-1");
        reply.CorrelationId.Should().Be("corr-1");
        reply.CausationId.Should().Be(context.MessageId.ToString("D"));
        var rejected = reply.Message.Should().BeOfType<AssetMutationBatchRejectedEvent>().Subject;
        rejected.BatchId.Should().Be(batch.BatchId);
        rejected.ReasonCode.Should().Be(expectedCode);
        rejected.Reason.Should().Be(failure.Message);
        rejected.ConflictingFeatureIds.Should().Equal(expectedConflicting);
        rejected.SourceVerticalId.Should().Be("neutral-operator");
        rejected.SourceOrderId.Should().Be("ord-1");
        rejected.RejectedAtUtc.Should().Be(_clock.GetUtcNow());
        _inbox.RecordedEntries.Should().ContainSingle("a rejection is a processed message too: it is not delivered again to be rejected again");
        _unitOfWork.Started.Should().Be(1);
    }

    [Fact]
    public async Task HandleAsync_ARejection_IsLoggedAsAWarning_WithTheVerticalAndTheCode_NeverTheFeatures()
    {
        var logs = new CapturingLogger();
        _applier.Failure = new AssetMutationFeatureNotFoundException("pole-secret", "Feature 'pole-secret' not found.");

        await Handler(logs).HandleAsync(Batch(), Context());

        var entry = logs.Entries.Should().ContainSingle().Subject;
        entry.Level.Should().Be(LogLevel.Warning);
        entry.Message.Should().Contain("neutral-operator").And.Contain(AssetMutationBatchRejectionCodes.FeatureNotFound).And.Contain("nothing was applied");
    }

    [Fact]
    public async Task HandleAsync_ARejectionDeliveredTwice_IsAnsweredOnce()
    {
        var batch = Batch();
        var context = Context();
        _applier.Failure = new MalformedBatchException("bad batch");
        var sut = Handler();

        await sut.HandleAsync(batch, context);
        await sut.HandleAsync(batch, context);

        _outbox.Messages.Should().ContainSingle();
        _applier.Received.Should().HaveCount(1);
    }

    // ── Retryable is not a rejection ──────────────────────────────────────────

    public static TheoryData<Exception> RetryableFailures() => new()
    {
        new TimeoutException("db timeout"),
        new InvalidOperationException("connection reset"),
        new IOException("socket closed"),
        new OperationCanceledException("cancelled"),
    };

    [Theory]
    [MemberData(nameof(RetryableFailures))]
    public async Task HandleAsync_AFailureThatMayPass_PropagatesAsIs_StagingNoRejection_RecordingNoInbox(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _applier.Failure = failure;

        var act = () => Handler().HandleAsync(Batch(), Context());

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(failure);
        _outbox.Messages.Should().BeEmpty("neither an answer nor a rejection");
        _inbox.RecordedEntries.Should().BeEmpty("the message must be delivered again");
        _unitOfWork.Started.Should().Be(0);
    }

    [Fact]
    public async Task HandleAsync_ACancelledToken_PropagatesTheCancellation_WithNothingRecordedOrStaged()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _applier.HonorCancellation = true;

        var act = () => Handler().HandleAsync(Batch(), Context(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _outbox.Messages.Should().BeEmpty();
        _inbox.RecordedEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_ABatchForAnotherCatalogInstance_IsNeitherAppliedNorRejected_ItIsNotThisHostsToAnswer()
    {
        var act = () => Handler().HandleAsync(Batch("another-catalog"), Context());

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("'another-catalog'").And.Contain($"'{CatalogInstanceId}'");
        _applier.Received.Should().BeEmpty();
        _outbox.Messages.Should().BeEmpty();
        _inbox.RecordedEntries.Should().BeEmpty();
    }

    // ── Trace ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAsync_StartsASagaApplySpan_TaggedWithTheBatchTheOrderAndTheOutcome()
    {
        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, GeoAssetsActivitySource.SourceName, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (spans) spans.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(listener);
        var batch = Batch();
        _applier.Failure = new NotSupportedException("no");

        await Handler().HandleAsync(batch, Context());

        Activity span;
        lock (spans)
        {
            span = spans.Single(a => string.Equals(a.OperationName, "Saga.AssetMutationApply", StringComparison.Ordinal)
                && string.Equals(a.GetTagItem("catalog.batch.id") as string, batch.BatchId.ToString("D"), StringComparison.Ordinal));
        }

        span.GetTagItem("order.id").Should().Be("ord-1");
        span.GetTagItem("organization.id").Should().Be(Organization.ToString("D"));
        span.GetTagItem("catalog.instance.id").Should().Be(CatalogInstanceId);
        span.GetTagItem("saga.outcome").Should().Be("rejected");
    }

    [Fact]
    public void Constructor_NullCollaborator_ThrowsArgumentNullException()
    {
        var applier = new KernelAssetMutationApplier(CatalogInstanceId, _applier);
        var logger = NullLogger<AssetMutationBatchApplyHandler>.Instance;

        ((Action)(() => _ = new AssetMutationBatchApplyHandler(null!, _inbox, _outbox, _unitOfWork, _scope, _tracer, _clock, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new AssetMutationBatchApplyHandler(applier, null!, _outbox, _unitOfWork, _scope, _tracer, _clock, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new AssetMutationBatchApplyHandler(applier, _inbox, null!, _unitOfWork, _scope, _tracer, _clock, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new AssetMutationBatchApplyHandler(applier, _inbox, _outbox, null!, _scope, _tracer, _clock, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new AssetMutationBatchApplyHandler(applier, _inbox, _outbox, _unitOfWork, null!, _tracer, _clock, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new AssetMutationBatchApplyHandler(applier, _inbox, _outbox, _unitOfWork, _scope, null!, _clock, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new AssetMutationBatchApplyHandler(applier, _inbox, _outbox, _unitOfWork, _scope, _tracer, null!, logger))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new AssetMutationBatchApplyHandler(applier, _inbox, _outbox, _unitOfWork, _scope, _tracer, _clock, null!))).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task HandleAsync_NullArguments_Throw()
    {
        var sut = Handler();

        await ((Func<Task>)(() => sut.HandleAsync(null!, Context()))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => sut.HandleAsync(Batch(), null!))).Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Doubles ───────────────────────────────────────────────────────────────

    /// <summary>An <see cref="ArgumentException"/> a kernel applier raises for a batch it cannot make sense of.</summary>
    private sealed class MalformedBatchException : ArgumentException
    {
        public MalformedBatchException()
        {
        }

        public MalformedBatchException(string message)
            : base(message)
        {
        }

        public MalformedBatchException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    private sealed class ScriptedApplier : IAssetMutationBatchApplier
    {
        public List<AssetMutationBatchRequestedEvent> Received { get; } = [];

        public Exception? Failure { get; set; }

        public bool HonorCancellation { get; set; }

        public Func<AssetMutationBatchRequestedEvent, AssetBatchAppliedEvent>? Result { get; set; }

        public Task<AssetBatchAppliedEvent> ApplyAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct)
        {
            if (HonorCancellation) ct.ThrowIfCancellationRequested();
            Received.Add(batch);
            if (Failure is not null) return Task.FromException<AssetBatchAppliedEvent>(Failure);

            return Task.FromResult(Result?.Invoke(batch) ?? AssetBatchAppliedEvent.Create(
                batch.BatchId, batch.CatalogInstanceId, batch.OrganizationId, batch.RequestedByService, "kernel", [], TimeProvider.System,
                sourceOrderId: batch.SourceOrderId, sourceOrderTypeId: batch.SourceOrderTypeId, correlationId: batch.CorrelationId,
                sourceVerticalId: batch.SourceVerticalId));
        }
    }

    private sealed class FailingUnitOfWork(IWorkflowUnitOfWork inner) : IWorkflowUnitOfWork
    {
        public int Failures { get; set; }

        public Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct)
        {
            if (Failures > 0)
            {
                Failures--;
                return Task.FromException<T>(new TimeoutException("the database went away before the commit"));
            }

            return inner.ExecuteAsync(work, ct);
        }
    }

    private sealed class CapturingLogger : ILogger<AssetMutationBatchApplyHandler>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
