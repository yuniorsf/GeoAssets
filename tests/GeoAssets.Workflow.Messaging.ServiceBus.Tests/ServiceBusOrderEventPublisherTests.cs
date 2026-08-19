using System.Diagnostics;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Workflow.Messaging.ServiceBus.Tests;

public class ServiceBusOrderEventPublisherTests
{
    private static readonly ActivitySource TestSource = new("GeoAssets.Workflow.Messaging.ServiceBus.Tests");

    static ServiceBusOrderEventPublisherTests()
    {
        // Without a registered listener, ActivitySource.StartActivity always
        // returns null (nothing is sampling), so tests couldn't produce a
        // real Activity to assert against.
        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
        });
    }

    [Fact]
    public void ApplyTraceContext_InjectsTraceparent_WhenActivityIsPresent()
    {
        using var activity = TestSource.StartActivity("test-publish", ActivityKind.Producer);
        activity.Should().NotBeNull("the static listener should force sampling");

        var applicationProperties = new Dictionary<string, object>();

        ServiceBusOrderEventPublisher.ApplyTraceContext(applicationProperties, activity);

        applicationProperties.Should().ContainKey("traceparent")
            .WhoseValue.Should().Be(activity!.Id);
    }

    [Fact]
    public void ApplyTraceContext_AddsNoKeys_WhenActivityIsNull()
    {
        var applicationProperties = new Dictionary<string, object>();

        ServiceBusOrderEventPublisher.ApplyTraceContext(applicationProperties, activity: null);

        applicationProperties.Should().BeEmpty();
    }

    [Fact]
    public void ApplyTraceContext_PreservesExistingProperties_AlongsideTraceparent()
    {
        using var activity = TestSource.StartActivity("test-publish", ActivityKind.Producer);

        var applicationProperties = new Dictionary<string, object>
        {
            ["orderId"]     = "order-1",
            ["orderTypeId"] = "inspection",
            ["newStatus"]   = "Closed",
        };

        ServiceBusOrderEventPublisher.ApplyTraceContext(applicationProperties, activity);

        applicationProperties.Should().Contain("orderId", "order-1");
        applicationProperties.Should().Contain("orderTypeId", "inspection");
        applicationProperties.Should().Contain("newStatus", "Closed");
        applicationProperties.Should().ContainKey("traceparent");
    }
}
