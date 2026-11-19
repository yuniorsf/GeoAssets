using GeoAssets.NeutralOperator.Application;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Messaging;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using GeoAssets.NeutralOperator.Persistence;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// The span and metrics <see cref="ObservableOccupancyCommitService"/> emits, and the
/// registration that puts it in place (XD01-318). Other test classes emit on the same
/// process-wide source and meter in parallel, so every assertion looks only at this test's own
/// telemetry: spans by operation name and this test's unique order id, measurements by this
/// test's unique meter version.
/// </summary>
public sealed class ObservableOccupancyCommitServiceTests : IDisposable
{
    private const string OrganizationId = "0f0f0f0f-0000-4000-8000-000000000318";

    private static readonly WorkflowPrincipal Principal = new("user-1", OrganizationId, ["Administrator"], [], []);

    private readonly string _orderId = $"ord-{Guid.NewGuid():N}";
    private readonly string _meterVersion = $"test-{Guid.NewGuid():N}";
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero));
    private readonly GeoAssetsActivitySource _tracer = new("1.0.0");
    private readonly GeoAssetsMeter _metrics;
    private readonly List<Activity> _activities = [];
    private readonly List<(string Instrument, double Value, Dictionary<string, object?> Tags)> _measurements = [];
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener = new();

    public ObservableOccupancyCommitServiceTests()
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
        _meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument.Name, value, tags));
        _meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument.Name, value, tags));
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

    private ObservableOccupancyCommitService Sut(StubCommit inner) => new(inner, _tracer, _metrics, _clock);

    private Activity Span()
    {
        lock (_activities)
        {
            return _activities.Single(a =>
                string.Equals(a.OperationName, "ServiceOrder.OccupancyCommit", StringComparison.Ordinal)
                && string.Equals(a.GetTagItem("order.id") as string, _orderId, StringComparison.Ordinal));
        }
    }

    private List<(double Value, Dictionary<string, object?> Tags)> Measured(string instrument)
    {
        lock (_measurements)
        {
            return [.. _measurements
                .Where(m => string.Equals(m.Instrument, instrument, StringComparison.Ordinal))
                .Select(m => (m.Value, m.Tags))];
        }
    }

    private OccupancyCommitResult Result(Dictionary<OccupancyChangeKind, int>? byKind = null)
    {
        byKind ??= new Dictionary<OccupancyChangeKind, int> { [OccupancyChangeKind.Add] = 3, [OccupancyChangeKind.MarkUnidentified] = 2 };
        return new OccupancyCommitResult
        {
            OrderId = _orderId,
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            PreviousStatus = ServiceOrderStatus.Completed,
            Status = SharedUseOrderTypes.ApprovedStateKey,
            ChangeSetsCommitted = 2,
            ChangeSetsDiscarded = 1,
            ChangesCommitted = byKind.Values.Sum(),
            ChangesByKind = byKind,
            PolesUpdated = 2,
        };
    }

    // ── Completed approval ────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAndCommitAsync_Completed_ReturnsTheInnerResult_AndTagsTheSpanWithWhatWasCommitted()
    {
        var expected = Result();
        var inner = new StubCommit { Result = expected };

        var actual = await Sut(inner).ApproveAndCommitAsync(_orderId, Principal, CancellationToken.None);

        actual.Should().BeSameAs(expected);
        inner.Calls.Should().ContainSingle().Which.Should().Be((_orderId, Principal));
        var span = Span();
        span.GetTagItem("organization.id").Should().Be(OrganizationId);
        span.GetTagItem("order.type").Should().Be(SharedUseOrderTypes.PoleWorkId);
        span.GetTagItem("occupancy.change_sets").Should().Be(2);
        span.GetTagItem("occupancy.changes").Should().Be(5);
        span.Status.Should().Be(ActivityStatusCode.Unset);
        span.GetTagItem("occupancy.outcome").Should().BeNull();
    }

    [Fact]
    public async Task ApproveAndCommitAsync_Completed_RecordsDurationFromTheInjectedClock_AndTheChangesPerKind_WithNoIdentifyingTag()
    {
        var inner = new StubCommit { Result = Result(), Elapse = () => _clock.Advance(TimeSpan.FromMilliseconds(120)) };

        await Sut(inner).ApproveAndCommitAsync(_orderId, Principal, CancellationToken.None);

        var duration = Measured("geoassets.isp.pole_occupancy.commit.duration").Should().ContainSingle().Subject;
        duration.Value.Should().Be(120);
        duration.Tags.Should().ContainSingle().Which.Should().Be(KeyValuePair.Create("outcome", (object?)ObservableOccupancyCommitService.OutcomeOk));
        Measured("geoassets.isp.pole_occupancy.changes_committed").Should().BeEquivalentTo(
        [
            (3d, new Dictionary<string, object?>(StringComparer.Ordinal) { ["kind"] = "Add" }),
            (2d, new Dictionary<string, object?>(StringComparer.Ordinal) { ["kind"] = "MarkUnidentified" }),
        ]);
        Measured("geoassets.isp.pole_occupancy.commit.conflicts").Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveAndCommitAsync_NothingCommitted_RecordsTheDuration_AndCountsNoChange()
    {
        var inner = new StubCommit { Result = Result(byKind: []) };

        await Sut(inner).ApproveAndCommitAsync(_orderId, Principal, CancellationToken.None);

        Measured("geoassets.isp.pole_occupancy.commit.duration").Should().ContainSingle();
        Measured("geoassets.isp.pole_occupancy.changes_committed").Should().BeEmpty();
    }

    // ── Failed approval ───────────────────────────────────────────────────────

    public static TheoryData<Exception, string> Failures => new()
    {
        { new OccupancyConflictException(["pole-1"]), ObservableOccupancyCommitService.OutcomeConflict },
        { new ServiceOrderConcurrencyException("ord"), ObservableOccupancyCommitService.OutcomeConflict },
        { new OrderActionDeniedException("ord", RuleEvaluationResult.Deny(OrderActionType.Approve, "no")), ObservableOccupancyCommitService.OutcomeRejected },
        { new KeyNotFoundException("no such order"), ObservableOccupancyCommitService.OutcomeRejected },
        { new InvalidServiceOrderTransitionException("Draft", "Draft"), ObservableOccupancyCommitService.OutcomeRejected },
        { InvalidArgument("bad"), ObservableOccupancyCommitService.OutcomeRejected },
        { new OperationCanceledException(), ObservableOccupancyCommitService.OutcomeCancelled },
        { new TimeoutException("database"), ObservableOccupancyCommitService.OutcomeError },
        { new InvalidOperationException("something else"), ObservableOccupancyCommitService.OutcomeError },
    };

    private static ArgumentException InvalidArgument(string orderId) => new("The order id is invalid.", nameof(orderId));

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task ApproveAndCommitAsync_Failed_RethrowsTheSameException_TagsTheOutcome_AndCountsOnlyConflicts(Exception failure, string outcome)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var inner = new StubCommit { Failure = failure };

        var act = () => Sut(inner).ApproveAndCommitAsync(_orderId, Principal, CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(failure);
        var span = Span();
        span.GetTagItem("occupancy.outcome").Should().Be(outcome);
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be(failure.GetType().Name, "a message could name poles or occupants");
        Measured("geoassets.isp.pole_occupancy.commit.duration").Should().ContainSingle()
            .Which.Tags.Should().ContainSingle().Which.Should().Be(KeyValuePair.Create("outcome", (object?)outcome));
        Measured("geoassets.isp.pole_occupancy.commit.conflicts").Should().HaveCount(
            string.Equals(outcome, ObservableOccupancyCommitService.OutcomeConflict, StringComparison.Ordinal) ? 1 : 0);
        Measured("geoassets.isp.pole_occupancy.changes_committed").Should().BeEmpty();
    }

    [Fact]
    public async Task Guards_NullCollaboratorsOrPrincipal_ThrowArgumentNullException_BeforeTheInnerServiceIsCalled()
    {
        var inner = new StubCommit { Result = Result() };

        FluentActions.Invoking(() => new ObservableOccupancyCommitService(null!, _tracer, _metrics)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ObservableOccupancyCommitService(inner, null!, _metrics)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ObservableOccupancyCommitService(inner, _tracer, null!)).Should().Throw<ArgumentNullException>();
        await FluentActions.Invoking(() => Sut(inner).ApproveAndCommitAsync(_orderId, null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        inner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task WithoutAClock_TheSystemClockTimesTheApproval()
    {
        var sut = new ObservableOccupancyCommitService(new StubCommit { Result = Result() }, _tracer, _metrics);

        await sut.ApproveAndCommitAsync(_orderId, Principal, CancellationToken.None);

        Measured("geoassets.isp.pole_occupancy.commit.duration").Should().ContainSingle().Which.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    // ── Registration ──────────────────────────────────────────────────────────

    [Fact]
    public async Task AddOccupancyCommitPersistence_DecoratesTheService_AndPutsTheUnitOfWorkOnTheScopesContext()
    {
        var services = new ServiceCollection();
        services.AddOrderTypeRegistry();
        services.AddServiceOrderRules();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddWorkflowPersistence(options => options.UseSqlite("Data Source=:memory:"));
        services.AddNeutralOperatorPersistence((options, connection) => options.UseSqlite(connection));
        services.AddSingleton<IIntegrationMessageSerializer>(new ProtobufIntegrationMessageSerializer());
        services.AddSingleton(_tracer);
        services.AddSingleton(_metrics);
        services.AddWorkflowOutboxStore();
        services.AddOccupancyCommit();
        services.AddOccupancyCommitPersistence();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        var resolved = scope.ServiceProvider.GetRequiredService<IOccupancyCommitService>();

        resolved.Should().BeOfType<ObservableOccupancyCommitService>();
        services.Count(d => d.ServiceType == typeof(IOccupancyCommitService)).Should().Be(1);
        scope.ServiceProvider.GetRequiredService<IWorkflowUnitOfWork>().Should().BeOfType<EFWorkflowUnitOfWork>();
        FluentActions.Invoking(() => NeutralOperatorPersistenceExtensions.AddOccupancyCommitPersistence(null!))
            .Should().Throw<ArgumentNullException>();
    }

    // ── Doubles ───────────────────────────────────────────────────────────────

    private sealed class StubCommit : IOccupancyCommitService
    {
        public OccupancyCommitResult? Result { get; init; }
        public Exception? Failure { get; init; }
        public Action? Elapse { get; init; }
        public List<(string OrderId, WorkflowPrincipal Principal)> Calls { get; } = [];

        public Task<OccupancyCommitResult> ApproveAndCommitAsync(string orderId, WorkflowPrincipal principal, CancellationToken ct)
        {
            Calls.Add((orderId, principal));
            Elapse?.Invoke();
            return Failure is not null ? Task.FromException<OccupancyCommitResult>(Failure) : Task.FromResult(Result!);
        }
    }
}
