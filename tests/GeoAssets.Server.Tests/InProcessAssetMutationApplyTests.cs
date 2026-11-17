using System.Diagnostics;
using System.Diagnostics.Metrics;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Services;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Provider.PostgreSQL.Repositories;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Persistence.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// How this host applies the asset mutation batches its outbox stages when it has no broker
/// (XD01-337): the in-process "publisher" the relay delivers to, the span and metrics around the
/// applier, and the registrations — present without a broker, absent with one. The database side
/// (tenant, idempotency, rollback) is covered by <c>PostgresAssetMutationBatchApplierTests</c> and,
/// end to end, by <c>OccupancyApprovalAppliedToTheCatalogTests</c>.
///
/// Other test classes emit on the same process-wide source and meter in parallel, so assertions
/// look only at this test's telemetry: spans by this test's batch id, measurements by this test's
/// meter version.
/// </summary>
public sealed class InProcessAssetMutationApplyTests : IDisposable
{
    private const string ConnectionString = "Host=localhost;Database=unused;Username=unused;Password=unused";

    private static readonly Guid Organization = new("0e0e0e0e-0000-4000-8000-000000000337");

    private readonly ProtobufIntegrationMessageSerializer _serializer = new();
    private readonly string _meterVersion = $"test-{Guid.NewGuid():N}";
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
    private readonly GeoAssetsActivitySource _tracer = new("1.0.0");
    private readonly GeoAssetsMeter _metrics;
    private readonly List<Activity> _activities = [];
    private readonly List<(string Instrument, double Value, Dictionary<string, object?> Tags)> _measurements = [];
    private readonly ActivityListener _activityListener;
    private readonly MeterListener _meterListener = new();

    public InProcessAssetMutationApplyTests()
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

    private List<(double Value, Dictionary<string, object?> Tags)> Measured(string instrument)
    {
        lock (_measurements)
        {
            return [.. _measurements
                .Where(m => string.Equals(m.Instrument, instrument, StringComparison.Ordinal))
                .Select(m => (m.Value, m.Tags))];
        }
    }

    private Activity Span(Guid batchId)
    {
        lock (_activities)
        {
            return _activities.Single(a =>
                string.Equals(a.OperationName, "Catalog.MutationBatchApply", StringComparison.Ordinal)
                && string.Equals(a.GetTagItem("catalog.batch.id") as string, batchId.ToString("D"), StringComparison.Ordinal));
        }
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();

    private static AssetMutationBatchRequestedEvent Batch(string catalogInstanceId = "default") =>
        AssetMutationBatchRequestedEvent.Create(
            catalogInstanceId: catalogInstanceId,
            organizationId: Organization,
            requestedByService: "tests",
            performedByUserId: "approver-1",
            mutations:
            [
                new AssetFeatureMutationItem(
                    "pole-1", AssetMutationKind.UpdateAttributes, string.Empty, "pole", string.Empty, string.Empty, null, [],
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["occupancyClass"] = "High" }),
            ],
            timeProvider: TimeProvider.System,
            batchId: Guid.NewGuid());

    private IntegrationEnvelope Envelope(AssetMutationBatchRequestedEvent batch, string messageType = IntegrationEventTypes.AssetMutationBatchRequestedV1) =>
        IntegrationEnvelope.Create(
            messageType: messageType,
            schemaVersion: IntegrationEventTypes.CurrentSchemaVersion,
            partitionKey: "order-1",
            contentType: _serializer.ContentType,
            payload: _serializer.Serialize(batch),
            timeProvider: TimeProvider.System,
            catalogInstanceId: batch.CatalogInstanceId);

    private static AssetBatchAppliedEvent Applied(AssetMutationBatchRequestedEvent batch) => AssetBatchAppliedEvent.Create(
        batchId: batch.BatchId,
        catalogInstanceId: batch.CatalogInstanceId,
        organizationId: batch.OrganizationId,
        requestedByService: batch.RequestedByService,
        appliedByUserId: batch.PerformedByUserId,
        appliedItems: [new AssetBatchAppliedItem("pole-1", AssetMutationKind.UpdateAttributes, "pole", [])],
        timeProvider: TimeProvider.System);

    // ── The in-process publisher ──────────────────────────────────────────────

    [Fact]
    public async Task PublishAsync_AnAssetBatchForThisCatalog_HandsTheDeserializedBatchToTheApplier()
    {
        var applier = new StubApplier();
        var batch = Batch();
        var sut = new InProcessAssetMutationBatchPublisher(_serializer, applier, "default");

        await sut.PublishAsync(Envelope(batch));

        var received = applier.Received.Should().ContainSingle().Subject;
        received.BatchId.Should().Be(batch.BatchId);
        received.OrganizationId.Should().Be(Organization);
        received.Mutations.Should().ContainSingle().Which.CustomAttributes.Should().Contain("occupancyClass", "High");
    }

    [Fact]
    public async Task PublishAsync_ABatchForAnotherCatalogInstance_IsRefused_AndNothingIsApplied()
    {
        var applier = new StubApplier();
        var sut = new InProcessAssetMutationBatchPublisher(_serializer, applier, "default");

        var act = () => sut.PublishAsync(Envelope(Batch("east")));

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*'east'*'default'*");
        applier.Received.Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_AnyOtherMessageType_IsRefused_AndNothingIsApplied()
    {
        var applier = new StubApplier();
        var sut = new InProcessAssetMutationBatchPublisher(_serializer, applier, "default");

        var act = () => sut.PublishAsync(Envelope(Batch(), IntegrationEventTypes.OrderStateChangedV1));

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage($"*{IntegrationEventTypes.OrderStateChangedV1}*");
        applier.Received.Should().BeEmpty();
    }

    [Fact]
    public Task PublishAsync_TheApplierFails_SurfacesTheFailure_SoTheRelayRetriesIt()
    {
        var sut = new InProcessAssetMutationBatchPublisher(_serializer, new StubApplier { Failure = new TimeoutException("db") }, "default");

        var act = () => sut.PublishAsync(Envelope(Batch()));

        return act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact]
    public async Task PublishBatchAsync_AppliesEveryEnvelopeInOrder()
    {
        var applier = new StubApplier();
        var first = Batch();
        var second = Batch();
        var sut = new InProcessAssetMutationBatchPublisher(_serializer, applier, "default");

        await sut.PublishBatchAsync([Envelope(first), Envelope(second)]);

        applier.Received.Select(b => b.BatchId).Should().Equal(first.BatchId, second.BatchId);
    }

    [Fact]
    public async Task Publisher_InvalidArguments_Throw()
    {
        var sut = new InProcessAssetMutationBatchPublisher(_serializer, new StubApplier(), "default");

        ((Action)(() => _ = new InProcessAssetMutationBatchPublisher(null!, new StubApplier(), "default")))
            .Should().Throw<ArgumentNullException>().WithParameterName("serializer");
        ((Action)(() => _ = new InProcessAssetMutationBatchPublisher(_serializer, null!, "default")))
            .Should().Throw<ArgumentNullException>().WithParameterName("applier");
        ((Action)(() => _ = new InProcessAssetMutationBatchPublisher(_serializer, new StubApplier(), " ")))
            .Should().Throw<ArgumentException>().WithParameterName("catalogInstanceId");
        await ((Func<Task>)(() => sut.PublishAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => sut.PublishBatchAsync(null!))).Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Span and metrics ──────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyAsync_Applied_TagsTheSpan_AndCountsAndTimesAnOkAttempt()
    {
        var batch = Batch();
        var inner = new StubApplier { OnApply = () => _clock.Advance(TimeSpan.FromMilliseconds(40)) };
        var sut = new ObservableAssetMutationBatchApplier(inner, _tracer, _metrics, _clock);

        var applied = await sut.ApplyAsync(batch, CancellationToken.None);

        applied.BatchId.Should().Be(batch.BatchId);
        var span = Span(batch.BatchId);
        span.GetTagItem("catalog.instance.id").Should().Be("default");
        span.GetTagItem("organization.id").Should().Be(Organization.ToString("D"));
        span.GetTagItem("catalog.batch.mutations").Should().Be(1);
        span.GetTagItem("catalog.batch.applied_items").Should().Be(1);
        span.Status.Should().Be(ActivityStatusCode.Unset);
        Measured("geoassets.catalog.mutation_batches").Should().ContainSingle()
            .Which.Should().Match<(double Value, Dictionary<string, object?> Tags)>(m =>
                m.Value == 1 && Equals(m.Tags["outcome"], ObservableAssetMutationBatchApplier.OutcomeOk) && m.Tags.Count == 1);
        Measured("geoassets.catalog.mutation_batch.duration").Should().ContainSingle()
            .Which.Value.Should().Be(40);
    }

    [Theory]
    [InlineData(typeof(KeyNotFoundException), ObservableAssetMutationBatchApplier.OutcomeRejected)]
    [InlineData(typeof(ArgumentException), ObservableAssetMutationBatchApplier.OutcomeRejected)]
    [InlineData(typeof(NotSupportedException), ObservableAssetMutationBatchApplier.OutcomeRejected)]
    [InlineData(typeof(GeoFeatureAttributeValidationException), ObservableAssetMutationBatchApplier.OutcomeRejected)]
    [InlineData(typeof(GeoFeatureGeometryMismatchException), ObservableAssetMutationBatchApplier.OutcomeRejected)]
    [InlineData(typeof(OperationCanceledException), ObservableAssetMutationBatchApplier.OutcomeCancelled)]
    [InlineData(typeof(TimeoutException), ObservableAssetMutationBatchApplier.OutcomeError)]
    [InlineData(typeof(InvalidOperationException), ObservableAssetMutationBatchApplier.OutcomeError)]
    public async Task ApplyAsync_Failed_MarksTheSpanWithTheExceptionTypeOnly_CountsTheOutcome_AndRethrows(Type exceptionType, string outcome)
    {
        ArgumentNullException.ThrowIfNull(exceptionType);
        var batch = Batch();
        var failure = (Exception)Activator.CreateInstance(exceptionType, "secret detail")!;
        var sut = new ObservableAssetMutationBatchApplier(new StubApplier { Failure = failure }, _tracer, _metrics, _clock);

        var act = () => sut.ApplyAsync(batch, CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.Should().BeSameAs(failure);
        var span = Span(batch.BatchId);
        span.Status.Should().Be(ActivityStatusCode.Error);
        span.StatusDescription.Should().Be(exceptionType.Name);
        span.GetTagItem("catalog.batch.outcome").Should().Be(outcome);
        Measured("geoassets.catalog.mutation_batches").Should().ContainSingle().Which.Tags["outcome"].Should().Be(outcome);
        Measured("geoassets.catalog.mutation_batch.duration").Should().ContainSingle().Which.Tags["outcome"].Should().Be(outcome);
    }

    [Fact]
    public Task Decorator_InvalidArguments_Throw()
    {
        var sut = new ObservableAssetMutationBatchApplier(new StubApplier(), _tracer, _metrics, _clock);

        ((Action)(() => _ = new ObservableAssetMutationBatchApplier(null!, _tracer, _metrics, _clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new ObservableAssetMutationBatchApplier(new StubApplier(), null!, _metrics, _clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new ObservableAssetMutationBatchApplier(new StubApplier(), _tracer, null!, _clock))).Should().Throw<ArgumentNullException>();
        ((Action)(() => _ = new ObservableAssetMutationBatchApplier(new StubApplier(), _tracer, _metrics, null!))).Should().Throw<ArgumentNullException>();
        return ((Func<Task>)(() => sut.ApplyAsync(null!, CancellationToken.None))).Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Host wiring ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("None")]
    [InlineData("none")]
    public void AddServerInProcessAssetMutationApply_WithNoTransport_RunsTheRelayOnAssetBatchesOnly_ThroughTheDecoratedPostgresApplier(string? transport)
    {
        var configuration = Configuration(
            (SharedUseHostExtensions.OutboxTransportKey, transport),
            (OrderOccupancyHostExtensions.CatalogInstanceIdKey, " west "));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton(_tracer);
        services.AddSingleton(_metrics);
        services.AddServerWorkflowOutbox(configuration);

        services.AddServerInProcessAssetMutationApply(configuration, ConnectionString);
        using var provider = services.BuildServiceProvider(validateScopes: true);

        provider.GetRequiredService<IAssetMutationBatchApplier>().Should().BeOfType<ObservableAssetMutationBatchApplier>();
        provider.GetRequiredService<PostgresAssetMutationBatchApplier>().Should().NotBeNull();
        provider.GetRequiredService<IIntegrationEventPublisher>().Should().BeOfType<InProcessAssetMutationBatchPublisher>();
        provider.GetServices<IHostedService>().Should().ContainSingle().Which.Should().BeOfType<OutboxRelayProcessor>();
        var options = provider.GetRequiredService<IOptions<OutboxRelayOptions>>().Value;
        options.WorkerId.Should().Be(InProcessAssetMutationHostExtensions.RelayWorkerId);
        options.MessageTypes.Should().Equal(IntegrationEventTypes.AssetMutationBatchRequestedV1);
    }

    [Fact]
    public async Task AddServerInProcessAssetMutationApply_ThePublisherServesTheConfiguredCatalogInstance()
    {
        var configuration = Configuration((OrderOccupancyHostExtensions.CatalogInstanceIdKey, " west "));
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton(_tracer);
        services.AddSingleton(_metrics);
        services.AddServerWorkflowOutbox(configuration);
        services.AddServerInProcessAssetMutationApply(configuration, ConnectionString);
        await using var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IIntegrationEventPublisher>().PublishAsync(Envelope(Batch("default")));

        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*'default'*'west'*");
    }

    [Theory]
    [InlineData("Kafka")]
    [InlineData("ServiceBus")]
    public void AddServerInProcessAssetMutationApply_WithABroker_RegistersNothing_SoTheBrokerRelayIsUnchanged(string transport)
    {
        var configuration = Configuration((SharedUseHostExtensions.OutboxTransportKey, transport));
        var services = new ServiceCollection();
        services.AddServerWorkflowOutbox(configuration);
        var before = services.Select(d => (d.ServiceType, d.ImplementationType)).ToList();

        services.AddServerInProcessAssetMutationApply(configuration, ConnectionString);

        services.Select(d => (d.ServiceType, d.ImplementationType)).Should().Equal(before);
        services.Should().NotContain(d => d.ServiceType == typeof(IAssetMutationBatchApplier));
    }

    [Fact]
    public void AddServerInProcessAssetMutationApply_InvalidArguments_Throw()
    {
        ((Action)(() => InProcessAssetMutationHostExtensions.AddServerInProcessAssetMutationApply(null!, Configuration(), ConnectionString)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new ServiceCollection().AddServerInProcessAssetMutationApply(null!, ConnectionString)))
            .Should().Throw<ArgumentNullException>();
        ((Action)(() => new ServiceCollection().AddServerInProcessAssetMutationApply(Configuration(), " ")))
            .Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(null, OrderOccupancyOptions.DefaultCatalogInstanceId)]
    [InlineData("  ", OrderOccupancyOptions.DefaultCatalogInstanceId)]
    [InlineData(" west ", "west")]
    public void CatalogInstanceId_IsTheTrimmedSetting_OrTheDefault(string? configured, string expected)
    {
        OrderOccupancyHostExtensions.CatalogInstanceId(Configuration((OrderOccupancyHostExtensions.CatalogInstanceIdKey, configured)))
            .Should().Be(expected);
        ((Action)(() => OrderOccupancyHostExtensions.CatalogInstanceId(null!))).Should().Throw<ArgumentNullException>();
    }

    private sealed class StubApplier : IAssetMutationBatchApplier
    {
        public List<AssetMutationBatchRequestedEvent> Received { get; } = [];

        public Exception? Failure { get; init; }

        public Action? OnApply { get; init; }

        public Task<AssetBatchAppliedEvent> ApplyAsync(AssetMutationBatchRequestedEvent batch, CancellationToken ct)
        {
            OnApply?.Invoke();
            if (Failure is not null)
                return Task.FromException<AssetBatchAppliedEvent>(Failure);

            Received.Add(batch);
            return Task.FromResult(Applied(batch));
        }
    }
}
