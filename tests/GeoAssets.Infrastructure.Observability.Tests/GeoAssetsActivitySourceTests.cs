using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Infrastructure.Observability.Tests;

public class GeoAssetsActivitySourceTests
{
    static GeoAssetsActivitySourceTests()
    {
        // Without a registered listener, ActivitySource.StartActivity always
        // returns null (nothing is sampling), so tests couldn't produce a
        // real Activity to assert against.
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = source => string.Equals(source.Name, GeoAssetsActivitySource.SourceName, StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        });
    }

    [Fact]
    public void StartNotificationActivity_DefaultsToInternalKind()
    {
        var tracer = new GeoAssetsActivitySource("1.0.0");

        using var activity = tracer.StartNotificationActivity("order-1", "kafka");

        activity.Should().NotBeNull();
        activity!.Kind.Should().Be(ActivityKind.Internal);
    }

    [Fact]
    public void StartNotificationActivity_UsesRequestedKind_AndSetsExpectedTags()
    {
        var tracer = new GeoAssetsActivitySource("1.0.0");

        using var activity = tracer.StartNotificationActivity("order-1", "kafka", ActivityKind.Producer);

        activity.Should().NotBeNull();
        activity!.Kind.Should().Be(ActivityKind.Producer);
        activity.GetTagItem("order.id").Should().Be("order-1");
        activity.GetTagItem("messaging.system").Should().Be("kafka");
    }
}
