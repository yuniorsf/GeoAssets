using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Targets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// The spans and metrics <see cref="ObservableOrderTargetSelectionService"/> emits (XD01-308).
/// Other test classes emit on the same process-wide source and meter in parallel, so every
/// assertion here looks only at this test's own telemetry: spans are picked by operation name
/// and this test's unique order id, measurements by this test's unique meter version.
/// </summary>
public sealed class ObservableOrderTargetSelectionServiceTests : IDisposable
{
    private const string OrganizationId = "0f0f0f0f-0000-4000-8000-000000000308";

    private static readonly WorkflowPrincipal Principal = new("user-1", OrganizationId, ["Administrator"], [], []);

    private readonly string _orderId = $"ord-{Guid.NewGuid():N}";
    private readonly string _meterVersion = $"test-{Guid.NewGuid():N}";
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 16, 10, 0, 0, TimeSpan.Zero));
    private readonly FeatureSelectionRegistry _registry;
    private readonly GeoAssetsActivitySource _tracer = new("1.0.0");
    private readonly GeoAssetsMeter _metrics;
    private readonly List<Activity> _activities = [];
    private readonly List<(string Instrument, double Value, Dictionary<string, object?> Tags)> _measurements = [];
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener = new();

    public ObservableOrderTargetSelectionServiceTests()
    {
        _registry = new FeatureSelectionRegistry(_clock, "no-such-plugins-directory", typeof(FeatureSelectionRegistry).Assembly);

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
        _registry.Dispose();
    }

    private void Record(string instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var copy = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags) copy[tag.Key] = tag.Value;
        lock (_measurements) _measurements.Add((instrument, value, copy));
    }

    private ObservableOrderTargetSelectionService Sut(StubSelectionService inner) =>
        new(inner, _registry, _tracer, _metrics, _clock);

    private TargetSelectionRequest Request(string strategyId = "polygon-fence") =>
        new(_orderId, strategyId, new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["rings"] = new double[][][] { [[-38.123456, -12.654321], [-38, -12.6], [-38, -12], [-38.123456, -12.654321]] },
        });

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

    private static TargetSelectionPreview Preview(int matched = 12, int truncated = 0) => new()
    {
        OrderTypeId = "pole-survey",
        Matched = matched,
        Eligible = matched - 2,
        AlreadyInOrder = 2,
        TruncatedByLimit = truncated,
        WarningCount = 3,
        BlockedCount = 1,
        FirstPage = new PagedResult<ServiceOrderTarget> { Items = [], TotalCount = matched - 2 - truncated },
    };

    private static TargetSelectionResult Result(int warnings = 3, int blocked = 1, int truncated = 0) => new()
    {
        OrderTypeId = "pole-survey",
        Added = 10,
        Matched = 12,
        Eligible = 10,
        AlreadyInOrder = 2,
        TruncatedByLimit = truncated,
        WarningCount = warnings,
        BlockedCount = blocked,
    };

    // ── Spans ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewAsync_EmitsThePreviewSpan_TaggedWithTheOrderStrategyOrganizationAndCounts()
    {
        var inner = new StubSelectionService { Preview = Preview() };

        var preview = await Sut(inner).PreviewAsync(Request(), Principal, CancellationToken.None);

        preview.Should().BeSameAs(inner.Preview);
        var span = SpanNamed("ServiceOrder.TargetSelection.Preview");
        span.GetTagItem("selection.strategy").Should().Be("polygon-fence");
        span.GetTagItem("organization.id").Should().Be(OrganizationId);
        span.GetTagItem("order.type").Should().Be("pole-survey");
        span.GetTagItem("selection.matched").Should().Be(12);
        span.GetTagItem("selection.eligible").Should().Be(10);
        span.GetTagItem("selection.already_in_order").Should().Be(2);
        span.GetTagItem("selection.truncated").Should().Be(0);
        span.GetTagItem("selection.conflicts.warning").Should().Be(3);
        span.GetTagItem("selection.conflicts.blocked").Should().Be(1);
        span.Status.Should().Be(ActivityStatusCode.Unset);
    }

    [Fact]
    public async Task ApplyAsync_EmitsTheApplySpan_AndForwardsTheRequestPrincipalAndToken()
    {
        var inner = new StubSelectionService { Result = Result() };
        using var cancellation = new CancellationTokenSource();
        var request = Request();

        var result = await Sut(inner).ApplyAsync(request, Principal, cancellation.Token);

        result.Should().BeSameAs(inner.Result);
        inner.Calls.Should().ContainSingle().Which.Should().Be(("apply", request, Principal, cancellation.Token));
        var span = SpanNamed("ServiceOrder.TargetSelection.Apply");
        span.GetTagItem("selection.matched").Should().Be(12);
        span.GetTagItem("order.type").Should().Be("pole-survey");
    }

    [Fact]
    public async Task Spans_NeverCarryTheSelectionsOwnValues()
    {
        var inner = new StubSelectionService { Preview = Preview() };

        await Sut(inner).PreviewAsync(Request(), Principal, CancellationToken.None);

        var tags = SpanNamed("ServiceOrder.TargetSelection.Preview").TagObjects.ToList();
        tags.Select(t => t.Key).Should().BeEquivalentTo(
            "order.id", "selection.strategy", "organization.id", "order.type", "selection.matched", "selection.eligible",
            "selection.already_in_order", "selection.truncated", "selection.conflicts.warning", "selection.conflicts.blocked");
        tags.Select(t => Convert.ToString(t.Value, System.Globalization.CultureInfo.InvariantCulture))
            .Should().NotContain(v => v!.Contains("38.123456", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FailedCall_MarksTheSpanAsError_WithTheExceptionTypeOnly_AndRethrowsTheSameException()
    {
        var failure = new InvalidCastException("identifier P-SECRET-42 is malformed");
        var inner = new StubSelectionService { Failure = failure };

        var act = () => Sut(inner).ApplyAsync(Request(), Principal, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidCastException>()).Which.Should().BeSameAs(failure);
        var span = SpanNamed("ServiceOrder.TargetSelection.Apply");
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be(nameof(InvalidCastException));
        span.GetTagItem("selection.outcome").Should().Be("rejected");
        span.TagObjects.Select(t => Convert.ToString(t.Value, System.Globalization.CultureInfo.InvariantCulture))
            .Should().NotContain(v => v!.Contains("P-SECRET-42", StringComparison.Ordinal));
        span.Events.Should().BeEmpty("the exception message may echo client input");
    }

    // ── Metrics ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewAsync_RecordsDurationFromTheInjectedClock_AndTheMatchedHistogram_ButNoConflicts()
    {
        var inner = new StubSelectionService { Preview = Preview(), OnCall = () => _clock.Advance(TimeSpan.FromMilliseconds(250)) };

        await Sut(inner).PreviewAsync(Request(), Principal, CancellationToken.None);

        var duration = Measured("geoassets.orders.targets.selection.duration").Should().ContainSingle().Subject;
        duration.Value.Should().Be(250);
        duration.Tags.Should().BeEquivalentTo(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["strategy"] = "polygon-fence", ["mode"] = "preview", ["outcome"] = "ok",
        });
        Measured("geoassets.orders.targets.selection.matched").Should().ContainSingle()
            .Which.Should().BeEquivalentTo((12d, new Dictionary<string, object?>(StringComparer.Ordinal) { ["strategy"] = "polygon-fence" }));
        Measured("geoassets.orders.targets.selection.truncated").Should().BeEmpty();
        Measured("geoassets.orders.targets.conflicts").Should().BeEmpty("conflicts are counted when targets are written");
    }

    [Fact]
    public async Task ApplyAsync_RecordsConflictsPerLevelAndOrderType_AndSkipsALevelWithNone()
    {
        var inner = new StubSelectionService { Result = Result(warnings: 3, blocked: 0) };

        await Sut(inner).ApplyAsync(Request(), Principal, CancellationToken.None);

        Measured("geoassets.orders.targets.conflicts").Should().ContainSingle()
            .Which.Should().BeEquivalentTo((3d, new Dictionary<string, object?>(StringComparer.Ordinal) { ["level"] = "warning", ["order.type"] = "pole-survey" }));
        Measured("geoassets.orders.targets.selection.duration").Single().Tags["mode"].Should().Be("apply");
    }

    [Fact]
    public async Task ApplyAsync_BlockedTargets_AreCountedAtLevelBlocked()
    {
        var inner = new StubSelectionService { Result = Result(warnings: 0, blocked: 4) };

        await Sut(inner).ApplyAsync(Request(), Principal, CancellationToken.None);

        Measured("geoassets.orders.targets.conflicts").Should().ContainSingle()
            .Which.Should().BeEquivalentTo((4d, new Dictionary<string, object?>(StringComparer.Ordinal) { ["level"] = "blocked", ["order.type"] = "pole-survey" }));
    }

    [Fact]
    public async Task TruncatedSelection_IsCounted_ForAPreviewThatReportsItAndForAnApplyThatIsRejectedForIt()
    {
        var previewing = new StubSelectionService { Preview = Preview(truncated: 5) };
        var rejecting = new StubSelectionService { Failure = new SelectionLimitExceededException(10, 8, 5) };

        await Sut(previewing).PreviewAsync(Request(), Principal, CancellationToken.None);
        var act = () => Sut(rejecting).ApplyAsync(Request(), Principal, CancellationToken.None);
        await act.Should().ThrowAsync<SelectionLimitExceededException>();

        Measured("geoassets.orders.targets.selection.truncated").Should().HaveCount(2)
            .And.OnlyContain(m => m.Value == 1 && Equals(m.Tags["strategy"], "polygon-fence"));
    }

    public static TheoryData<Exception, string> Outcomes() => new()
    {
        { new SelectionTooLargeException(50_001, 50_000), "too_large" },
        { new SelectionLimitExceededException(10, 8, 5), "rejected" },
        { new OrderActionDeniedException("ord-1", RuleEvaluationResult.Deny(OrderActionType.Annotate, "no")), "rejected" },
        { new KeyNotFoundException("no order"), "rejected" },
        { new InvalidSelectionFenceException(SelectionFenceDefect.NotClosed), "rejected" },
        { new InvalidCastException("rings"), "rejected" },
        { new ServiceOrderConcurrencyException("ord-1"), "rejected" },
        { new InvalidServiceOrderTransitionException("Completed", "Completed"), "rejected" },
        { new OperationCanceledException(), "cancelled" },
        { new TimeoutException("database"), "error" },
    };

    [Theory]
    [MemberData(nameof(Outcomes))]
    public async Task FailedCall_RecordsTheDurationWithTheOutcomeOfTheFailure(Exception failure, string expectedOutcome)
    {
        var inner = new StubSelectionService { Failure = failure };

        var act = () => Sut(inner).PreviewAsync(Request(), Principal, CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
        Measured("geoassets.orders.targets.selection.duration").Should().ContainSingle()
            .Which.Tags["outcome"].Should().Be(expectedOutcome);
        Measured("geoassets.orders.targets.selection.matched").Should().BeEmpty();
    }

    [Fact]
    public async Task StrategyTag_IsTheRegisteredId_AndAnUnregisteredIdBecomesUnknown_NeverTheCallersString()
    {
        var inner = new StubSelectionService { Preview = Preview() };
        var sut = Sut(inner);

        await sut.PreviewAsync(Request("POLYGON-FENCE"), Principal, CancellationToken.None);
        await sut.PreviewAsync(Request("made-up-by-a-client-" + Guid.NewGuid()), Principal, CancellationToken.None);

        Measured("geoassets.orders.targets.selection.duration").Select(m => m.Tags["strategy"])
            .Should().Equal("polygon-fence", "unknown");
    }

    [Fact]
    public async Task Metrics_AreNeverTaggedWithAnOrganizationOrderOrFeatureId()
    {
        var applying = new StubSelectionService { Result = Result(truncated: 2) };

        await Sut(applying).ApplyAsync(Request(), Principal, CancellationToken.None);

        List<(string Instrument, double Value, Dictionary<string, object?> Tags)> all;
        lock (_measurements) all = [.. _measurements];
        all.Should().NotBeEmpty();
        all.SelectMany(m => m.Tags.Keys).Distinct(StringComparer.Ordinal).Should().BeSubsetOf(["strategy", "mode", "outcome", "level", "order.type"]);
        all.SelectMany(m => m.Tags.Values).Should().NotContain(OrganizationId).And.NotContain(_orderId);
    }

    // ── Guards and registration ───────────────────────────────────────────────

    [Fact]
    public async Task NullArguments_ThrowArgumentNullException()
    {
        var inner = new StubSelectionService();
        var sut = Sut(inner);

        FluentActions.Invoking(() => new ObservableOrderTargetSelectionService(null!, _registry, _tracer, _metrics)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ObservableOrderTargetSelectionService(inner, null!, _tracer, _metrics)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ObservableOrderTargetSelectionService(inner, _registry, null!, _metrics)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => new ObservableOrderTargetSelectionService(inner, _registry, _tracer, null!)).Should().Throw<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.PreviewAsync(null!, Principal, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => sut.ApplyAsync(Request(), null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        inner.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AddObservableOrderTargetSelection_ReplacesTheInterfaceRegistration_WithTheDecoratorOverTheConcreteService()
    {
        var services = new ServiceCollection();
        services.AddOrderTypeRegistry();
        services.AddServiceOrderRules();
        services.AddLogging();
        services.AddWorkflowPersistence(options => options.UseSqlite("Data Source=:memory:"));
        services.AddSingleton<GeoAssets.Core.Interfaces.IAssetProvider>(new TestAssetProvider());
        services.AddSingleton(_tracer);
        services.AddSingleton(_metrics);
        services.AddFeatureSelection();
        services.AddOrderTargets();
        services.AddObservableOrderTargetSelection();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await using var scope = provider.CreateAsyncScope();

        var resolved = scope.ServiceProvider.GetRequiredService<IOrderTargetSelectionService>();

        resolved.Should().BeOfType<ObservableOrderTargetSelectionService>();
        services.Count(d => d.ServiceType == typeof(IOrderTargetSelectionService)).Should().Be(1);
        scope.ServiceProvider.GetRequiredService<OrderTargetSelectionService>().Should().NotBeNull();
        FluentActions.Invoking(() => WorkflowEFCoreServiceExtensions.AddObservableOrderTargetSelection(null!))
            .Should().Throw<ArgumentNullException>();
    }

    // ── Doubles ───────────────────────────────────────────────────────────────

    private sealed class StubSelectionService : IOrderTargetSelectionService
    {
        public TargetSelectionPreview? Preview { get; init; }
        public TargetSelectionResult? Result { get; init; }
        public Exception? Failure { get; init; }
        public Action? OnCall { get; init; }
        public List<(string Mode, TargetSelectionRequest Request, WorkflowPrincipal Principal, CancellationToken Token)> Calls { get; } = [];

        public Task<TargetSelectionPreview> PreviewAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct)
        {
            Calls.Add(("preview", request, principal, ct));
            OnCall?.Invoke();
            return Failure is null ? Task.FromResult(Preview!) : Task.FromException<TargetSelectionPreview>(Failure);
        }

        public Task<TargetSelectionResult> ApplyAsync(TargetSelectionRequest request, WorkflowPrincipal principal, CancellationToken ct)
        {
            Calls.Add(("apply", request, principal, ct));
            OnCall?.Invoke();
            return Failure is null ? Task.FromResult(Result!) : Task.FromException<TargetSelectionResult>(Failure);
        }
    }
}
