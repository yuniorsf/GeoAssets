using System.Diagnostics;
using System.Text;
using FluentAssertions;
using GeoAssets.Workflow.Notifications;
using Xunit;

namespace GeoAssets.Workflow.Messaging.Kafka.Tests;

public class KafkaOrderEventPublisherTests
{
    private static readonly ActivitySource TestSource = new("GeoAssets.Workflow.Messaging.Kafka.Tests");

    static KafkaOrderEventPublisherTests()
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

    private static OrderStateChangedEvent CreateEvent(string? correlationId = "corr-1") => new(
        OrderId       : "order-1",
        OrderTypeId   : "inspection",
        PreviousStatus: "Open",
        NewStatus     : "Closed",
        PerformedBy   : "user-1",
        OccurredAt    : DateTimeOffset.UtcNow,
        CorrelationId : correlationId);

    [Fact]
    public void BuildHeaders_InjectsTraceparent_WhenActivityIsPresent()
    {
        using var activity = TestSource.StartActivity("test-publish", ActivityKind.Producer);
        activity.Should().NotBeNull("the static listener should force sampling");

        var headers = KafkaOrderEventPublisher.BuildHeaders(CreateEvent(), activity);

        var traceparent = headers.Should().ContainSingle(h => h.Key == "traceparent").Which;
        Encoding.UTF8.GetString(traceparent.GetValueBytes()).Should().Be(activity!.Id);
    }

    [Fact]
    public void BuildHeaders_OmitsTraceparent_WhenActivityIsNull()
    {
        var headers = KafkaOrderEventPublisher.BuildHeaders(CreateEvent(), activity: null);

        headers.Should().NotContain(h => h.Key == "traceparent");
    }

    [Fact]
    public void BuildHeaders_PreservesExistingHeaders_AlongsideTraceparent()
    {
        using var activity = TestSource.StartActivity("test-publish", ActivityKind.Producer);

        var headers = KafkaOrderEventPublisher.BuildHeaders(CreateEvent(correlationId: "corr-42"), activity);

        Encoding.UTF8.GetString(headers.Single(h => h.Key == "orderTypeId").GetValueBytes()).Should().Be("inspection");
        Encoding.UTF8.GetString(headers.Single(h => h.Key == "newStatus").GetValueBytes()).Should().Be("Closed");
        Encoding.UTF8.GetString(headers.Single(h => h.Key == "correlationId").GetValueBytes()).Should().Be("corr-42");
        headers.Should().Contain(h => h.Key == "traceparent");
    }

    [Fact]
    public void BuildHeaders_OmitsCorrelationIdHeader_WhenNotProvided()
    {
        var headers = KafkaOrderEventPublisher.BuildHeaders(CreateEvent(correlationId: null), activity: null);

        headers.Should().NotContain(h => h.Key == "correlationId");
    }
}
