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
                if (instrument.Meter.Name == GeoAssetsMeter.MeterName)
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

        capture.Measurements.Should().ContainSingle(m => m.Instrument == "geoassets.orders.transitions");
        var measurement = capture.Measurements.Single(m => m.Instrument == "geoassets.orders.transitions");
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
