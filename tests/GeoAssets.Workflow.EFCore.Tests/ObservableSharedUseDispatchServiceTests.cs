using GeoAssets.NeutralOperator.Application;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Outbox;
using GeoAssets.Workflow.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using GeoAssets.NeutralOperator.Persistence;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// The span and metrics <see cref="ObservableSharedUseDispatchService"/> emits, the child spans
/// of <see cref="ActivitySharedUseFanOutTracer"/>, and the registrations that put both in place
/// (XD01-316). Other test classes emit on the same process-wide source and meter in parallel,
/// so every assertion looks only at this test's own telemetry: spans by operation name and this
/// test's unique order id, measurements by this test's unique meter version.
/// </summary>
public sealed class ObservableSharedUseDispatchServiceTests : IDisposable
{
    private const string OrganizationId = "0f0f0f0f-0000-4000-8000-000000000316";

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

    public ObservableSharedUseDispatchServiceTests()
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

    private ObservableSharedUseDispatchService Sut(StubDispatch inner) => new(inner, _tracer, _metrics, _clock);

    private Activity SpanNamed(string operationName)
    {
        lock (_activities)
        {
            return _activities.Single(a =>
                string.Equals(a.OperationName, operationName, StringComparison.Ordinal)
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

    private SharedUseDispatchResult Result(int created = 3, int unnotifiable = 2) => new()
    {
        ParentOrderId = _orderId,
        OrderTypeId = SharedUseOrderTypes.PoleWorkId,
        ParentStatus = ServiceOrderStatus.Pending,
        ChildOrdersCreated = created,
        ChildOrdersAlreadyDispatched = 1,
        NotificationsEnqueued = created,
        TargetCount = 9,
        BlockedTargetCount = 1,
        UnnotifiablePoleCount = unnotifiable,
        SkippedEntryCount = 4,
    };

    // ── Completed dispatch ────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_Completed_ReturnsTheInnerResult_AndTagsTheSpanWithWhatTheDispatchDid()
    {
        var expected = Result();
        var inner = new StubDispatch { Result = expected, Elapse = () => _clock.Advance(TimeSpan.FromMilliseconds(250)) };
        byte[] rowVersion = [5];

        var actual = await Sut(inner).DispatchAsync(_orderId, rowVersion, Principal, CancellationToken.None);

        actual.Should().BeSameAs(expected);
        inner.Calls.Should().ContainSingle().Which.Should().Be((_orderId, rowVersion, Principal));
        var span = SpanNamed("ServiceOrder.FanOutDispatch");
        span.GetTagItem("organization.id").Should().Be(OrganizationId);
        span.GetTagItem("order.type").Should().Be(SharedUseOrderTypes.PoleWorkId);
        span.GetTagItem("fanout.children").Should().Be(3);
        span.GetTagItem("fanout.unnotifiable_poles").Should().Be(2);
        span.Status.Should().Be(ActivityStatusCode.Unset);
        span.GetTagItem("fanout.outcome").Should().BeNull();
    }

    [Fact]
    public async Task DispatchAsync_Completed_RecordsDurationFromTheInjectedClock_AndTheChildrenByOrderType_WithNoIdentifyingTag()
    {
        var inner = new StubDispatch { Result = Result(created: 3), Elapse = () => _clock.Advance(TimeSpan.FromMilliseconds(250)) };

        await Sut(inner).DispatchAsync(_orderId, [], Principal, CancellationToken.None);

        var duration = Measured("geoassets.orders.fanout.duration").Should().ContainSingle().Subject;
        duration.Value.Should().Be(250);
        duration.Tags.Should().ContainSingle().Which.Should().Be(KeyValuePair.Create("outcome", (object?)ObservableSharedUseDispatchService.OutcomeOk));
        var children = Measured("geoassets.orders.fanout.children_spawned").Should().ContainSingle().Subject;
        children.Value.Should().Be(3);
        children.Tags.Should().ContainSingle().Which.Should().Be(KeyValuePair.Create("order.type", (object?)SharedUseOrderTypes.PoleWorkId));
    }

    [Fact]
    public async Task DispatchAsync_AReplayThatCreatedNothing_RecordsTheDuration_AndCountsNoChild()
    {
        var inner = new StubDispatch { Result = Result(created: 0) };

        await Sut(inner).DispatchAsync(_orderId, [], Principal, CancellationToken.None);

        Measured("geoassets.orders.fanout.duration").Should().ContainSingle();
        Measured("geoassets.orders.fanout.children_spawned").Should().BeEmpty();
        SpanNamed("ServiceOrder.FanOutDispatch").GetTagItem("fanout.children").Should().Be(0);
    }

    // ── Failed dispatch ───────────────────────────────────────────────────────

    public static TheoryData<Exception, string> Failures => new()
    {
        { new SharedUseDispatchBlockedException("ord", SharedUseDispatchBlockReason.NoTargets), ObservableSharedUseDispatchService.OutcomeBlocked },
        { new OrderActionDeniedException("ord", RuleEvaluationResult.Deny(OrderActionType.Dispatch, "no")), ObservableSharedUseDispatchService.OutcomeRejected },
        { new KeyNotFoundException("no such order"), ObservableSharedUseDispatchService.OutcomeRejected },
        { new OrderTypeNotRegisteredException("ghost", "no such type"), ObservableSharedUseDispatchService.OutcomeRejected },
        { new OrderTypeHasNoFanOutException("plain", "no fan-out"), ObservableSharedUseDispatchService.OutcomeRejected },
        { new InvalidServiceOrderTransitionException("Completed", "Completed"), ObservableSharedUseDispatchService.OutcomeRejected },
        { new ServiceOrderConcurrencyException("ord"), ObservableSharedUseDispatchService.OutcomeRejected },
        { InvalidArgument("bad"), ObservableSharedUseDispatchService.OutcomeRejected },
        { new OperationCanceledException(), ObservableSharedUseDispatchService.OutcomeCancelled },
        { new TimeoutException("database"), ObservableSharedUseDispatchService.OutcomeError },
        { new InvalidOperationException("something else"), ObservableSharedUseDispatchService.OutcomeError },
    };

    private static ArgumentException InvalidArgument(string orderId) => new("The order id is invalid.", nameof(orderId));

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task DispatchAsync_Failed_RethrowsTheSameException_TagsTheOutcome_AndRecordsOnlyTheExceptionsType(Exception failure, string outcome)
    {
        ArgumentNullException.ThrowIfNull(failure);
        var inner = new StubDispatch { Failure = failure };

        var act = () => Sut(inner).DispatchAsync(_orderId, [], Principal, CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(failure);
        var span = SpanNamed("ServiceOrder.FanOutDispatch");
        span.GetTagItem("fanout.outcome").Should().Be(outcome);
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be(failure.GetType().Name, "a message could echo what the caller sent");
        span.Events.Should().BeEmpty();
        Measured("geoassets.orders.fanout.duration").Should().ContainSingle()
            .Which.Tags.Should().ContainSingle().Which.Should().Be(KeyValuePair.Create("outcome", (object?)outcome));
        Measured("geoassets.orders.fanout.children_spawned").Should().BeEmpty();
    }

    [Fact]
    public async Task Guards_NullCollaboratorsOrPrincipal_ThrowArgumentNullException_BeforeTheInnerServiceIsCalled()
    {
        var inner = new StubDispatch { Result = Result() };

        FluentActions.Invoking(() => new ObservableSharedUseDispatchService(null!, _tracer, _metrics)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ObservableSharedUseDispatchService(inner, null!, _metrics)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ObservableSharedUseDispatchService(inner, _tracer, null!)).Should().Throw<ArgumentNullException>();
        await FluentActions.Invoking(() => Sut(inner).DispatchAsync(_orderId, [], null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        inner.Calls.Should().BeEmpty();
    }

    // ── Child spans ───────────────────────────────────────────────────────────

    [Fact]
    public void FanOutTracer_StartsOneChildSpan_TaggedWithTheChildItsParentAndItsOccupant_AndMakesItCurrent()
    {
        var occupant = Guid.NewGuid();
        var sut = new ActivitySharedUseFanOutTracer(_tracer);

        using (var span = sut.BeginChild("parent-1", _orderId, occupant))
        {
            span.Should().BeSameAs(Activity.Current, "what is staged on the outbox now carries this span as its trace parent");
        }

        var child = SpanNamed("ServiceOrder.FanOutChild");
        child.GetTagItem("order.parent_id").Should().Be("parent-1");
        child.GetTagItem("fanout.party_organization.id").Should().Be(occupant.ToString("D"));
    }

    [Fact]
    public async Task ADispatchOnTheEfStores_StagesEachNotificationWithItsChildSpanAsTraceParent_UnderTheDispatchSpan()
    {
        var owner = Guid.Parse(OrganizationId);
        using var fixture = new SqliteFixture(new FakeTenantContext(owner));
        SharedUseDispatchEfHarness.ReloadOrdersAfterEachSave(fixture.Context);
        var ef = new SharedUseDispatchEfHarness(fixture.Context, owner, clock: _clock);
        SharedUseOrderTypes.RegisterAll(ef.OrderTypes);
        var occupant = Guid.NewGuid();
        await ef.Orders.AddAsync(new ServiceOrder
        {
            Id = _orderId,
            Title = "Shared use",
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            CreatedBy = "planner-1",
            OrganizationId = owner,
            CreatedAt = _clock.GetUtcNow().UtcDateTime,
        });
        await ef.Targets.AddRangeAsync(
            _orderId,
            [new TestKit.ServiceOrderTargetBuilder()
                .WithId(Targets.OrderTargetSelectionService.TargetId(_orderId, "pole-1")).WithOrderId(_orderId).WithFeatureId("pole-1")
                .WithActivityIds("relocate-attachment").WithOrganizationId(owner).Build()],
            CancellationToken.None);
        await ef.Occupancy.ReplaceEntriesAsync(
            "pole-1", 0,
            [new TestKit.PoleOccupancyEntryBuilder().ForPole("pole-1").OwnedBy(owner).OnSlot(1).OccupiedBy(occupant).Build()],
            CancellationToken.None);
        var traced = new SharedUseDispatchService(
            ef.Orders, ef.OrderTypes, ef.Rules, ef.Targets, ef.Targets, ef.Occupancy,
            new EFOutboxStore(fixture.Context, _clock, new ProtobufIntegrationMessageSerializer()), ef.UnitOfWork, _clock,
            new ActivitySharedUseFanOutTracer(_tracer));
        var sut = new ObservableSharedUseDispatchService(traced, _tracer, _metrics, _clock);

        await sut.DispatchAsync(_orderId, [], Principal, CancellationToken.None);

        var dispatchSpan = SpanNamed("ServiceOrder.FanOutDispatch");
        Activity childSpan;
        lock (_activities)
        {
            childSpan = _activities.Single(a =>
                string.Equals(a.OperationName, "ServiceOrder.FanOutChild", StringComparison.Ordinal)
                && string.Equals(a.GetTagItem("order.parent_id") as string, _orderId, StringComparison.Ordinal));
        }

        childSpan.ParentSpanId.Should().Be(dispatchSpan.SpanId);
        childSpan.GetTagItem("order.id").Should().Be(SharedUseDispatchPlanner.ChildOrderId(_orderId, occupant));
        var staged = await fixture.Context.OutboxMessages.AsNoTracking().SingleAsync();
        staged.TraceParent.Should().Be(childSpan.Id, "the relay continues the child's trace");
    }

    // ── Registration ──────────────────────────────────────────────────────────

    private ServiceCollection HostServices()
    {
        var services = new ServiceCollection();
        services.AddOrderTypeRegistry();
        services.AddServiceOrderRules();
        services.AddLogging();
        services.AddWorkflowPersistence(options => options.UseSqlite("Data Source=:memory:"));
        services.AddNeutralOperatorPersistence((options, connection) => options.UseSqlite(connection));
        services.AddSingleton<IIntegrationMessageSerializer>(new ProtobufIntegrationMessageSerializer());
        services.AddSingleton(_tracer);
        services.AddSingleton(_metrics);
        return services;
    }

    [Fact]
    public async Task AddSharedUseDispatchPersistence_DecoratesTheService_AndPutsTheUnitOfWorkOnTheScopesContext()
    {
        var services = HostServices();
        services.AddWorkflowOutboxStore();
        services.AddSharedUseDispatch();
        services.AddSharedUseDispatchPersistence();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        var resolved = scope.ServiceProvider.GetRequiredService<ISharedUseDispatchService>();

        resolved.Should().BeOfType<ObservableSharedUseDispatchService>();
        services.Count(d => d.ServiceType == typeof(ISharedUseDispatchService)).Should().Be(1);
        scope.ServiceProvider.GetRequiredService<ISharedUseDispatchPlanReader>().Should().BeOfType<SharedUseDispatchService>()
            .And.BeSameAs(scope.ServiceProvider.GetRequiredService<SharedUseDispatchService>());
        scope.ServiceProvider.GetRequiredService<IWorkflowUnitOfWork>().Should().BeOfType<EFWorkflowUnitOfWork>()
            .And.BeSameAs(scope.ServiceProvider.GetRequiredService<IWorkflowUnitOfWork>());
        provider.GetRequiredService<ISharedUseFanOutTracer>().Should().BeOfType<ActivitySharedUseFanOutTracer>();
        FluentActions.Invoking(() => WorkflowEFCoreServiceExtensions.AddSharedUseDispatchPersistence(null!))
            .Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task AddWorkflowOutboxStore_RegistersTheWriterTheDispatcherStoreAndTheInbox_ButNoRelay()
    {
        var services = HostServices();
        services.AddWorkflowOutboxStore();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IOutboxWriter>();

        writer.Should().BeOfType<EFOutboxStore>().And.BeSameAs(scope.ServiceProvider.GetRequiredService<IOutboxDispatcherStore>());
        scope.ServiceProvider.GetRequiredService<IIdempotentInbox>().Should().BeOfType<EFIdempotentInbox>();
        services.Should().NotContain(
            d => d.ServiceType == typeof(IHostedService),
            "without a publisher there is nothing to relay to; staged messages wait in the table");
        FluentActions.Invoking(() => WorkflowEFCoreServiceExtensions.AddWorkflowOutboxStore(null!)).Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AddWorkflowOutbox_StillRegistersTheStoreAndTheRelay()
    {
        var services = HostServices();

        services.AddWorkflowOutbox();

        services.Should().ContainSingle(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(OutboxRelayProcessor));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IOutboxWriter));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IOutboxDispatcherStore));
        services.Should().ContainSingle(d => d.ServiceType == typeof(IIdempotentInbox));
    }

    // ── Doubles ───────────────────────────────────────────────────────────────

    private sealed class StubDispatch : ISharedUseDispatchService
    {
        public SharedUseDispatchResult? Result { get; init; }
        public Exception? Failure { get; init; }
        public Action? Elapse { get; init; }
        public List<(string OrderId, byte[] RowVersion, WorkflowPrincipal Principal)> Calls { get; } = [];

        public Task<SharedUseDispatchResult> DispatchAsync(
            string orderId, byte[] expectedRowVersion, WorkflowPrincipal principal, CancellationToken ct)
        {
            Calls.Add((orderId, expectedRowVersion, principal));
            Elapse?.Invoke();
            return Failure is not null ? Task.FromException<SharedUseDispatchResult>(Failure) : Task.FromResult(Result!);
        }
    }
}
