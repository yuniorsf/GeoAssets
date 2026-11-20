using System.Diagnostics.Metrics;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Infrastructure.Observability.Tests;

public class GeoAssetsMeterTests
{
    /// <summary>Captures every measurement recorded on <see cref="GeoAssetsMeter.MeterName"/> while listening.</summary>
    private sealed class MeasurementCapture : IDisposable
    {
        private readonly MeterListener _listener = new();
        public List<(string Instrument, double Value, KeyValuePair<string, object?>[] Tags)> Measurements { get; } = [];

        public MeasurementCapture()
        {
            _listener.InstrumentPublished = (instrument, listener) =>
            {
                if (string.Equals(instrument.Meter.Name, GeoAssetsMeter.MeterName, StringComparison.Ordinal))
                    listener.EnableMeasurementEvents(instrument);
            };
            _listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
                Measurements.Add((instrument.Name, measurement, tags.ToArray())));
            _listener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
                Measurements.Add((instrument.Name, measurement, tags.ToArray())));
            _listener.Start();
        }

        public void Dispose() => _listener.Dispose();
    }

    [Fact]
    public void RecordOrderTransition_IncrementsCounterWithExpectedTags()
    {
        using var meter = new GeoAssetsMeter("1.0.0");
        using var capture = new MeasurementCapture();

        meter.RecordOrderTransition("inspection", "Draft", "Pending");

        capture.Measurements.Should().ContainSingle(m => string.Equals(m.Instrument, "geoassets.orders.transitions", StringComparison.Ordinal));
        var measurement = capture.Measurements.Single(m => string.Equals(m.Instrument, "geoassets.orders.transitions", StringComparison.Ordinal));
        measurement.Value.Should().Be(1);
        measurement.Tags.Should().Contain(new KeyValuePair<string, object?>("order.type", "inspection"));
        measurement.Tags.Should().Contain(new KeyValuePair<string, object?>("order.prev_status", "Draft"));
        measurement.Tags.Should().Contain(new KeyValuePair<string, object?>("order.new_status", "Pending"));
    }

    [Fact]
    public void RecordNotificationPublished_IncrementsCounterWithTransportTag()
    {
        using var meter = new GeoAssetsMeter("1.0.0");
        using var capture = new MeasurementCapture();

        meter.RecordNotificationPublished("kafka");

        var measurement = capture.Measurements.Should().ContainSingle(m => m.Instrument == "geoassets.notifications.published").Subject;
        measurement.Value.Should().Be(1);
        measurement.Tags.Should().Contain(new KeyValuePair<string, object?>("transport", "kafka"));
    }

    [Fact]
    public void RecordNotificationDuration_RecordsHistogramValueWithTransportTag()
    {
        using var meter = new GeoAssetsMeter("1.0.0");
        using var capture = new MeasurementCapture();

        meter.RecordNotificationDuration(42.5, "servicebus");

        var measurement = capture.Measurements.Should().ContainSingle(m => m.Instrument == "geoassets.notifications.duration").Subject;
        measurement.Value.Should().Be(42.5);
        measurement.Tags.Should().Contain(new KeyValuePair<string, object?>("transport", "servicebus"));
    }

    [Fact]
    public void RecordFtthDropInstallDuration_RecordsHistogramValue()
    {
        using var meter = new GeoAssetsMeter("1.0.0");
        using var capture = new MeasurementCapture();

        meter.RecordFtthDropInstallDuration(3_600);

        var measurement = capture.Measurements.Should().ContainSingle(m => m.Instrument == "geoassets.isp.ftth_drop_install.duration").Subject;
        measurement.Value.Should().Be(3_600);
    }

    [Theory]
    [InlineData("Passed")]
    [InlineData("GuardBlocked")]
    public void RecordOpticalBudgetOutcome_IncrementsCounterWithOutcomeTag(string outcome)
    {
        using var meter = new GeoAssetsMeter("1.0.0");
        using var capture = new MeasurementCapture();

        meter.RecordOpticalBudgetOutcome(outcome);

        var measurement = capture.Measurements.Should().ContainSingle(m => m.Instrument == "geoassets.isp.optical_budget.outcomes").Subject;
        measurement.Value.Should().Be(1);
        measurement.Tags.Should().Contain(new KeyValuePair<string, object?>("outcome", outcome));
    }

    [Fact]
    public void RecordUtilityCutOutageDispatched_IncrementsCounterWithNoTags()
    {
        using var meter = new GeoAssetsMeter("1.0.0");
        using var capture = new MeasurementCapture();

        meter.RecordUtilityCutOutageDispatched();

        var measurement = capture.Measurements.Should().ContainSingle(m => m.Instrument == "geoassets.isp.utility_cut.outages_dispatched").Subject;
        measurement.Value.Should().Be(1);
        measurement.Tags.Should().BeEmpty();
    }

    [Fact]
    public void RecordFaultDispatchLatency_RecordsHistogramValue()
    {
        using var meter = new GeoAssetsMeter("1.0.0");
        using var capture = new MeasurementCapture();

        meter.RecordFaultDispatchLatency(128.4);

        var measurement = capture.Measurements.Should().ContainSingle(m => m.Instrument == "geoassets.isp.fault.dispatch_latency").Subject;
        measurement.Value.Should().Be(128.4);
    }

    [Theory]
    [InlineData(3, 3)]
    [InlineData(-3, 3)]
    [InlineData(0, 0)]
    public void RecordCtoOccupancyDiscrepancy_RecordsAbsoluteMagnitude(int difference, double expectedRecorded)
    {
        using var meter = new GeoAssetsMeter("1.0.0");
        using var capture = new MeasurementCapture();

        meter.RecordCtoOccupancyDiscrepancy(difference);

        var measurement = capture.Measurements.Should().ContainSingle(m => m.Instrument == "geoassets.isp.cto.occupancy_discrepancy").Subject;
        measurement.Value.Should().Be(expectedRecorded);
    }

    /// <summary>
    /// XD01-219's cardinality-safety requirement: organization.id/tenant id must never be a
    /// metric tag, since a metric series is keyed by its full tag set and a per-tenant tag would
    /// multiply every instrument's series count by the tenant count. Asserts this across every
    /// recording method this meter exposes today, so a future addition that tags a new
    /// instrument with a tenant identifier fails this test rather than shipping unnoticed.
    /// </summary>
    [Fact]
    public void NoInstrument_EverTagsAnOrganizationOrTenantIdentifier()
    {
        using var meter = new GeoAssetsMeter("1.0.0");
        using var capture = new MeasurementCapture();

        meter.RecordOrderTransition("inspection", "Draft", "Pending");
        meter.RecordNotificationPublished("kafka");
        meter.RecordNotificationDuration(42.5, "servicebus");
        meter.RecordFtthDropInstallDuration(3_600);
        meter.RecordOpticalBudgetOutcome("Passed");
        meter.RecordUtilityCutOutageDispatched();
        meter.RecordFaultDispatchLatency(128.4);
        meter.RecordCtoOccupancyDiscrepancy(3);

        capture.Measurements.Should().NotBeEmpty();
        foreach (var measurement in capture.Measurements)
        {
            measurement.Tags.Select(t => t.Key).Should().NotContain(
                key => key.Contains("organization", StringComparison.OrdinalIgnoreCase)
                    || key.Contains("tenant", StringComparison.OrdinalIgnoreCase),
                $"instrument '{measurement.Instrument}' must never carry a per-tenant tag — that belongs on a span attribute, not a metric dimension");
        }
    }

    [Fact]
    public void Dispose_DisposesUnderlyingMeter_FurtherRecordingsAreNoOps()
    {
        var meter = new GeoAssetsMeter("1.0.0");
        using var capture = new MeasurementCapture();

        meter.Dispose();
        var act = () => meter.RecordOrderTransition("inspection", "Draft", "Pending");

        act.Should().NotThrow();
        capture.Measurements.Should().BeEmpty();
    }

    // ── Approval saga (XD01-383) ──────────────────────────────────────────────

    [Fact]
    public void RecordSagaApplyTimeouts_AddsTheNumberOfSagasFlagged_NothingForNone()
    {
        using var meter = new GeoAssetsMeter("1.0.0");
        using var capture = new MeasurementCapture();

        meter.RecordSagaApplyTimeouts(0);
        meter.RecordSagaApplyTimeouts(-3);
        meter.RecordSagaApplyTimeouts(4);

        capture.Measurements.Should().ContainSingle(m => m.Instrument == "geoassets.saga.apply.timeouts").Which.Value.Should().Be(4);
    }

    [Fact]
    public void RecordSagaApplyOutcome_CountsTheOutcome_AndTimesOnlyWhenADurationIsGiven()
    {
        using var meter = new GeoAssetsMeter("1.0.0");
        using var capture = new MeasurementCapture();

        meter.RecordSagaApplyOutcome("committed", 12.5);
        meter.RecordSagaApplyOutcome("duplicate", null);

        var outcomes = capture.Measurements.Where(m => string.Equals(m.Instrument, "geoassets.saga.apply.outcomes", StringComparison.Ordinal)).ToList();
        outcomes.Should().HaveCount(2);
        outcomes[0].Tags.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, object?>("outcome", "committed"));
        outcomes[1].Tags.Should().ContainSingle().Which.Should().Be(new KeyValuePair<string, object?>("outcome", "duplicate"));
        var durations = capture.Measurements.Where(m => string.Equals(m.Instrument, "geoassets.saga.apply.duration", StringComparison.Ordinal)).ToList();
        durations.Should().ContainSingle().Which.Value.Should().Be(12.5);
    }

    [Fact]
    public void RecordSagaApplyOutcome_ANegativeDuration_IsRecordedAsZero()
    {
        using var meter = new GeoAssetsMeter("1.0.0");
        using var capture = new MeasurementCapture();

        meter.RecordSagaApplyOutcome("reverted", -5);

        capture.Measurements.Should().ContainSingle(m => m.Instrument == "geoassets.saga.apply.duration").Which.Value.Should().Be(0);
    }

    [Fact]
    public void RecordSagaApplyCompensationConflict_CountsOneWithTheOrderTypeAsTheOnlyTag()
    {
        using var meter = new GeoAssetsMeter("1.0.0");
        using var capture = new MeasurementCapture();

        meter.RecordSagaApplyCompensationConflict("pole-work");

        var measurement = capture.Measurements.Should().ContainSingle(m => m.Instrument == "geoassets.saga.apply.compensation_conflicts").Subject;
        measurement.Value.Should().Be(1);
        measurement.Tags.Should().HaveCount(1).And.Contain(new KeyValuePair<string, object?>("order.type", "pole-work"));
    }

    [Fact]
    public void CompensationConflictsInstrument_IsACounterWithTheSagasUnit()
    {
        var published = new List<(string Name, string? Unit, Type Type)>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, _) =>
        {
            if (string.Equals(instrument.Meter.Name, GeoAssetsMeter.MeterName, StringComparison.Ordinal)
                && string.Equals(instrument.Name, "geoassets.saga.apply.compensation_conflicts", StringComparison.Ordinal))
            {
                published.Add((instrument.Name, instrument.Unit, instrument.GetType()));
            }
        };
        listener.Start();

        using var meter = new GeoAssetsMeter("1.0.0");

        published.Should().ContainSingle().Which.Should().Match<(string Name, string? Unit, Type Type)>(i =>
            i.Unit == "{sagas}" && i.Type == typeof(Counter<long>));
    }

    [Fact]
    public void SetSagaApplyBacklog_IsWhatTheGaugesReport_ClampedAtZero()
    {
        var version = $"backlog-{Guid.NewGuid():N}";
        using var meter = new GeoAssetsMeter(version);
        using var listener = new MeterListener();
        var readings = new List<(string Instrument, double Value)>();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (string.Equals(instrument.Meter.Name, GeoAssetsMeter.MeterName, StringComparison.Ordinal)
                && string.Equals(instrument.Meter.Version, version, StringComparison.Ordinal)
                && instrument.Name.StartsWith("geoassets.saga.apply.", StringComparison.Ordinal))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, _, _) => readings.Add((instrument.Name, value)));
        listener.SetMeasurementEventCallback<double>((instrument, value, _, _) => readings.Add((instrument.Name, value)));
        listener.Start();

        meter.SetSagaApplyBacklog(7, 321.5);
        listener.RecordObservableInstruments();
        var first = readings.ToList();
        readings.Clear();
        meter.SetSagaApplyBacklog(-2, -9);
        listener.RecordObservableInstruments();

        first.Should().Contain(("geoassets.saga.apply.pending", 7));
        first.Should().Contain(("geoassets.saga.apply.oldest_pending_age", 321.5));
        readings.Should().Contain(("geoassets.saga.apply.pending", 0));
        readings.Should().Contain(("geoassets.saga.apply.oldest_pending_age", 0));
    }
}
