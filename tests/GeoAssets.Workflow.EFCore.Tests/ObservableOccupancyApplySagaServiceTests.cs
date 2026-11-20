using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// The span, metrics and logs <see cref="ObservableOccupancyApplySagaService"/> puts around the approval saga's replies and retry
/// (XD01-383): the outcome of each reply as a fixed tag — never a batch, order or organization id — the saga's duration once it
/// ended, one warning per compensation, and an error when the kernel applied what the vertical had already rolled back. Other test
/// classes emit on the same process-wide source and meter in parallel, so assertions look only at this test's own telemetry: spans
/// by its unique order id, measurements by its unique meter version.
/// </summary>
public sealed class ObservableOccupancyApplySagaServiceTests : IDisposable
{
    private static readonly Guid Organization = new("0e0e0e0e-0000-4000-8000-000000000383");

    private readonly string _orderId = $"ord-{Guid.NewGuid():N}";
    private readonly string _meterVersion = $"saga-{Guid.NewGuid():N}";
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero));
    private readonly GeoAssetsActivitySource _tracer = new("1.0.0");
    private readonly GeoAssetsMeter _metrics;
    private readonly ScriptedSagaService _inner = new();
    private readonly CapturingLogger _logs = new();
    private readonly List<Activity> _activities = [];
    private readonly List<(string Instrument, double Value, Dictionary<string, object?> Tags)> _measurements = [];
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener = new();

    public ObservableOccupancyApplySagaServiceTests()
    {
        _activityListener = new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, GeoAssetsActivitySource.SourceName, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity =>
            {
                lock (_activities) _activities.Add(activity);
            },
        };
        ActivitySource.AddActivityListener(_activityListener);

        _meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (string.Equals(instrument.Meter.Name, GeoAssetsMeter.MeterName, StringComparison.Ordinal)
                && string.Equals(instrument.Meter.Version, _meterVersion, StringComparison.Ordinal))
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument.Name, value, tags));
        _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument.Name, value, tags));
        _meterListener.Start();
        _metrics = new GeoAssetsMeter(_meterVersion);
    }

    public void Dispose()
    {
        _activityListener.Dispose();
        _meterListener.Dispose();
        _metrics.Dispose();
        _tracer.Dispose();
    }

    private void Record(string instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags) copy[tag.Key] = tag.Value;
        lock (_measurements) _measurements.Add((instrument, value, copy));
    }

    private List<(double Value, Dictionary<string, object?> Tags)> Measured(string instrument)
    {
        lock (_measurements)
        {
            return [.. _measurements.Where(m => string.Equals(m.Instrument, instrument, StringComparison.Ordinal)).Select(m => (m.Value, m.Tags))];
        }
    }

    private Activity Span(string operation)
    {
        lock (_activities)
        {
            return _activities.Single(a => string.Equals(a.OperationName, operation, StringComparison.Ordinal)
                && string.Equals(a.GetTagItem("order.id") as string, _orderId, StringComparison.Ordinal));
        }
    }

    private ObservableOccupancyApplySagaService Sut() => new(_inner, _tracer, _metrics, _logs);

    private AssetBatchAppliedEvent Applied(Guid? batchId = null) => AssetBatchAppliedEvent.Create(
        batchId ?? Guid.NewGuid(), "catalog", Organization, "tests", "kernel", [], _clock, sourceOrderId: _orderId, sourceVerticalId: "neutral-operator");

    private AssetMutationBatchRejectedEvent Rejected(Guid? batchId = null) => new(
        batchId ?? Guid.NewGuid(), "catalog", Organization, "tests", _orderId, null, null, AssetMutationBatchRejectionCodes.FeatureNotFound,
        "Feature 'pole-secret' not found.", ["pole-secret"], _clock.GetUtcNow(), null, "neutral-operator");

    private OccupancyApplySaga Saga(OccupancyApplySagaState state, TimeSpan duration) => new()
    {
        BatchId = Guid.NewGuid(),
        Attempt = 1,
        OrderId = _orderId,
        OrderTypeId = "pole-work",
        OrganizationId = Organization,
        PreviousStatus = "Completed",
        ApprovedStatus = "Approved",
        RequestedBy = "approver-1",
        RequestedAt = _clock.GetUtcNow() - duration,
        LastRequestedAt = _clock.GetUtcNow() - duration,
        State = state,
        CompletedAt = _clock.GetUtcNow(),
        Batch = AssetMutationBatchRequestedEvent.Create("catalog", Organization, "tests", "approver-1", [], _clock, sourceOrderId: _orderId),
    };

    private static IntegrationMessageContext Context() => new IntegrationMessageContextBuilder().WithMessageId(Guid.NewGuid()).Build();

    [Fact]
    public async Task HandleAppliedAsync_ACommittedSaga_TagsTheSpan_CountsTheOutcome_AndTimesTheSaga()
    {
        _inner.Result = new OccupancyApplySagaResult(OccupancyApplySagaOutcome.Committed, Saga(OccupancyApplySagaState.Committed, TimeSpan.FromSeconds(42)));
        var applied = Applied();

        var result = await Sut().HandleAppliedAsync(applied, Context(), CancellationToken.None);

        result.Outcome.Should().Be(OccupancyApplySagaOutcome.Committed);
        var span = Span("Saga.AssetMutationApplied");
        span.GetTagItem("catalog.batch.id").Should().Be(applied.BatchId.ToString("D"));
        span.GetTagItem("organization.id").Should().Be(Organization.ToString("D"));
        span.GetTagItem("saga.outcome").Should().Be("committed");
        span.Status.Should().Be(ActivityStatusCode.Unset);
        Measured("geoassets.saga.apply.outcomes").Should().ContainSingle().Which.Should().Match<(double Value, Dictionary<string, object?> Tags)>(m =>
            m.Value == 1 && Equals(m.Tags["outcome"], "committed") && m.Tags.Count == 1);
        Measured("geoassets.saga.apply.duration").Should().ContainSingle().Which.Value.Should().Be(42);
        _logs.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleRejectedAsync_ARevertedSaga_TagsTheSpan_CountsTheOutcome_TimesTheSaga_AndWarnsOnceWithTheCodeOnly()
    {
        _inner.Result = new OccupancyApplySagaResult(OccupancyApplySagaOutcome.Reverted, Saga(OccupancyApplySagaState.Reverted, TimeSpan.FromSeconds(7)));
        var rejected = Rejected();

        var result = await Sut().HandleRejectedAsync(rejected, Context(), CancellationToken.None);

        result.Outcome.Should().Be(OccupancyApplySagaOutcome.Reverted);
        Span("Saga.AssetMutationRejected").GetTagItem("saga.outcome").Should().Be("reverted");
        Measured("geoassets.saga.apply.outcomes").Should().ContainSingle().Which.Tags["outcome"].Should().Be("reverted");
        Measured("geoassets.saga.apply.duration").Should().ContainSingle().Which.Value.Should().Be(7);
        var warning = _logs.Entries.Should().ContainSingle().Subject;
        warning.Level.Should().Be(LogLevel.Warning);
        warning.Message.Should().Contain(AssetMutationBatchRejectionCodes.FeatureNotFound).And.Contain(_orderId).And.NotContain("pole-secret", "the reason and features stay on the order's log, not in the telemetry");
    }

    [Theory]
    [InlineData(OccupancyApplySagaOutcome.Duplicate, "duplicate")]
    [InlineData(OccupancyApplySagaOutcome.Ignored, "ignored")]
    public async Task HandleAppliedAsync_ADuplicateOrIgnoredReply_IsCounted_WithNoDuration_AndNoLog(OccupancyApplySagaOutcome outcome, string tag)
    {
        _inner.Result = new OccupancyApplySagaResult(outcome);

        await Sut().HandleAppliedAsync(Applied(), Context(), CancellationToken.None);

        Measured("geoassets.saga.apply.outcomes").Should().ContainSingle().Which.Tags["outcome"].Should().Be(tag);
        Measured("geoassets.saga.apply.duration").Should().BeEmpty("only a reply that ended a saga times it");
        _logs.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAppliedAsync_AnAppliedAnswerAfterTheSagaReverted_IsCountedAsAppliedAfterRevert_AndLoggedAsAnError()
    {
        _inner.Result = new OccupancyApplySagaResult(OccupancyApplySagaOutcome.AppliedAfterRevert, Saga(OccupancyApplySagaState.Reverted, TimeSpan.FromMinutes(20)));
        var applied = Applied();

        await Sut().HandleAppliedAsync(applied, Context(), CancellationToken.None);

        Measured("geoassets.saga.apply.outcomes").Should().ContainSingle().Which.Tags["outcome"].Should().Be("applied-after-revert");
        Measured("geoassets.saga.apply.duration").Should().BeEmpty("the saga ended when it reverted");
        Span("Saga.AssetMutationApplied").GetTagItem("saga.outcome").Should().Be("applied-after-revert");
        var error = _logs.Entries.Should().ContainSingle().Subject;
        error.Level.Should().Be(LogLevel.Error);
        error.Message.Should().Contain(applied.BatchId.ToString()).And.Contain(_orderId).And.Contain("Reconcile by hand");
    }

    [Fact]
    public async Task HandleAppliedAsync_WhenTheSagaFails_MarksTheSpanWithTheExceptionTypeOnly_CountsNoOutcome_AndRethrows()
    {
        var failure = new TimeoutException("secret detail");
        _inner.Failure = failure;

        var act = () => Sut().HandleAppliedAsync(Applied(), Context(), CancellationToken.None);

        (await act.Should().ThrowAsync<TimeoutException>()).Which.Should().BeSameAs(failure);
        var span = Span("Saga.AssetMutationApplied");
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be(nameof(TimeoutException));
        Measured("geoassets.saga.apply.outcomes").Should().BeEmpty();
    }

    [Fact]
    public async Task RetryAsync_TagsTheRetrySpan_LogsIt_AndReturnsTheView()
    {
        var batchId = Guid.NewGuid();
        _inner.View = new OccupancyApplySagaView(batchId, _orderId, 1, OccupancyApplySagaState.Pending, _clock.GetUtcNow(), _clock.GetUtcNow(), 3, null);
        var principal = new WorkflowPrincipal("approver-1", Organization.ToString(), ["Supervisor"], [], []);

        var view = await Sut().RetryAsync(_orderId, principal, CancellationToken.None);

        view.RetryCount.Should().Be(3);
        var span = Span("Saga.AssetMutationRetry");
        span.GetTagItem("catalog.batch.id").Should().Be(batchId.ToString("D"));
        span.GetTagItem("saga.retry_count").Should().Be(3);
        _logs.Entries.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
    }

    [Fact]
    public async Task RetryAsync_WhenItIsRefused_MarksTheSpanWithTheExceptionType_AndRethrows()
    {
        _inner.Failure = new OccupancyApplyNotPendingException(_orderId, OccupancyApplySagaState.Committed);
        var principal = new WorkflowPrincipal("approver-1", Organization.ToString(), ["Supervisor"], [], []);

        var act = () => Sut().RetryAsync(_orderId, principal, CancellationToken.None);

        await act.Should().ThrowAsync<OccupancyApplyNotPendingException>();
        Span("Saga.AssetMutationRetry").Status.Should().Be(ActivityStatusCode.Error);
    }

    [Theory]
    [InlineData(OccupancyApplySagaOutcome.Committed, "committed")]
    [InlineData(OccupancyApplySagaOutcome.Reverted, "reverted")]
    [InlineData(OccupancyApplySagaOutcome.Duplicate, "duplicate")]
    [InlineData(OccupancyApplySagaOutcome.AppliedAfterRevert, "applied-after-revert")]
    [InlineData(OccupancyApplySagaOutcome.Ignored, "ignored")]
    public void OutcomeTag_IsAFixedVocabulary(OccupancyApplySagaOutcome outcome, string expected)
    {
        ObservableOccupancyApplySagaService.OutcomeTag(outcome).Should().Be(expected);
    }

    [Fact]
    public void Constructor_NullCollaborator_ThrowsArgumentNullException()
    {
        ((Action)(() => _ = new ObservableOccupancyApplySagaService(null!, _tracer, _metrics, _logs))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new ObservableOccupancyApplySagaService(_inner, null!, _metrics, _logs))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new ObservableOccupancyApplySagaService(_inner, _tracer, null!, _logs))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new ObservableOccupancyApplySagaService(_inner, _tracer, _metrics, null!))).Should().Throw<ArgumentNullException>();
    }

    private sealed class ScriptedSagaService : IOccupancyApplySagaService
    {
        public OccupancyApplySagaResult Result { get; set; } = OccupancyApplySagaResult.Ignored;

        public OccupancyApplySagaView? View { get; set; }

        public Exception? Failure { get; set; }

        public Task<OccupancyApplySagaResult> HandleAppliedAsync(AssetBatchAppliedEvent applied, IntegrationMessageContext context, CancellationToken ct) =>
            Failure is not null ? Task.FromException<OccupancyApplySagaResult>(Failure) : Task.FromResult(Result);

        public Task<OccupancyApplySagaResult> HandleRejectedAsync(AssetMutationBatchRejectedEvent rejected, IntegrationMessageContext context, CancellationToken ct) =>
            Failure is not null ? Task.FromException<OccupancyApplySagaResult>(Failure) : Task.FromResult(Result);

        public Task<OccupancyApplySagaView> RetryAsync(string orderId, WorkflowPrincipal principal, CancellationToken ct) =>
            Failure is not null ? Task.FromException<OccupancyApplySagaView>(Failure) : Task.FromResult(View!);
    }

    private sealed class CapturingLogger : ILogger<ObservableOccupancyApplySagaService>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
