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

    [Fact]
    public void StartSagaActivity_NamesTheStepUnderSaga_AndTagsTheBatchTheOrderAndTheOrganization()
    {
        var tracer = new GeoAssetsActivitySource("1.0.0");
        var batchId = Guid.NewGuid();

        using var activity = tracer.StartSagaActivity("AssetMutationApply", batchId, "order-1", "org-1");

        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("Saga.AssetMutationApply");
        activity.GetTagItem("catalog.batch.id").Should().Be(batchId.ToString("D"));
        activity.GetTagItem("order.id").Should().Be("order-1");
        activity.GetTagItem("organization.id").Should().Be("org-1");
    }

    [Fact]
    public void StartSagaActivity_WithNoIds_TagsNothing()
    {
        var tracer = new GeoAssetsActivitySource("1.0.0");

        using var activity = tracer.StartSagaActivity("TimeoutSweep");

        activity!.OperationName.Should().Be("Saga.TimeoutSweep");
        activity.GetTagItem("catalog.batch.id").Should().BeNull();
        activity.GetTagItem("order.id").Should().BeNull();
        activity.GetTagItem("organization.id").Should().BeNull();
    }

    [Fact]
    public void StartSagaActivity_InsideAnotherSpan_IsItsChild_SoTheApprovalIsOneTrace()
    {
        var tracer = new GeoAssetsActivitySource("1.0.0");
        using var parent = tracer.StartNotificationActivity("order-1", "kafka");

        using var child = tracer.StartSagaActivity("AssetMutationApplied", orderId: "order-1");

        child!.TraceId.Should().Be(parent!.TraceId);
        child.ParentSpanId.Should().Be(parent.SpanId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void StartSagaActivity_ABlankStep_ThrowsArgumentException(string step)
    {
        var tracer = new GeoAssetsActivitySource("1.0.0");

        var act = () => tracer.StartSagaActivity(step);

        act.Should().Throw<ArgumentException>();
    }
}
