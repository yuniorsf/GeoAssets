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
}
