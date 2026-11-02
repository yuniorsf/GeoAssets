using System.Globalization;
using FluentAssertions;
using GeoAssets.Core.Isp;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.FieldWork;
using Xunit;

namespace GeoAssets.Workflow.Tests.FieldWork;

public class CtoOccupancyMutationFactoryTests
{
    private static GeoFeature CtoFeature(string id, int occupied, int total) =>
        new()
        {
            Id = id,
            Properties = new GeoFeatureProperties
            {
                CustomAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [CtoCapacityCalculator.OccupiedSplitterPortsKey] = occupied.ToString(CultureInfo.InvariantCulture),
                    [CtoCapacityCalculator.TotalSplitterPortsKey] = total.ToString(CultureInfo.InvariantCulture),
                },
            },
        };

    [Fact]
    public void BuildAdjustmentMutation_InstallWithinCapacity_Succeeds()
    {
        var feature = CtoFeature("cto-1", occupied: 5, total: 10);

        var result = CtoOccupancyMutationFactory.BuildAdjustmentMutation(feature, delta: 1);

        result.Succeeded.Should().BeTrue();
        result.NewOccupiedCount.Should().Be(6);
        result.Mutation.Should().NotBeNull();
        result.Mutation!.CustomAttributes[CtoCapacityCalculator.OccupiedSplitterPortsKey].Should().Be("6");
        result.Mutation.MutationKind.Should().Be(AssetMutationKind.UpdateAttributes);
        result.Mutation.FeatureId.Should().Be("cto-1");
    }

    [Fact]
    public void BuildAdjustmentMutation_RemovalWithinCapacity_Succeeds()
    {
        var feature = CtoFeature("cto-1", occupied: 5, total: 10);

        var result = CtoOccupancyMutationFactory.BuildAdjustmentMutation(feature, delta: -1);

        result.Succeeded.Should().BeTrue();
        result.NewOccupiedCount.Should().Be(4);
        result.Mutation!.CustomAttributes[CtoCapacityCalculator.OccupiedSplitterPortsKey].Should().Be("4");
    }

    [Fact]
    public void BuildAdjustmentMutation_DeltaWouldGoNegative_FailsWithReasonMentioningAttempt()
    {
        var feature = CtoFeature("cto-1", occupied: 0, total: 10);

        var result = CtoOccupancyMutationFactory.BuildAdjustmentMutation(feature, delta: -1);

        result.Succeeded.Should().BeFalse();
        result.Mutation.Should().BeNull();
        result.NewOccupiedCount.Should().BeNull();
        result.FailureReason.Should().Contain("below zero");
    }

    [Fact]
    public void BuildAdjustmentMutation_DeltaWouldExceedPositiveTotal_Fails()
    {
        var feature = CtoFeature("cto-1", occupied: 9, total: 10);

        var result = CtoOccupancyMutationFactory.BuildAdjustmentMutation(feature, delta: 5);

        result.Succeeded.Should().BeFalse();
        result.Mutation.Should().BeNull();
        result.NewOccupiedCount.Should().BeNull();
        result.FailureReason.Should().Contain("exceed total splitter capacity");
    }

    [Fact]
    public void BuildAdjustmentMutation_LargeDeltaWithUnsetTotal_SucceedsWithNoUpperBound()
    {
        var feature = CtoFeature("cto-1", occupied: 0, total: 0);

        var result = CtoOccupancyMutationFactory.BuildAdjustmentMutation(feature, delta: 1000);

        result.Succeeded.Should().BeTrue();
        result.NewOccupiedCount.Should().Be(1000);
        result.Mutation!.CustomAttributes[CtoCapacityCalculator.OccupiedSplitterPortsKey].Should().Be("1000");
    }

    [Fact]
    public void BuildAdjustmentMutation_NullCtoFeature_ThrowsArgumentNullException()
    {
        var act = () => CtoOccupancyMutationFactory.BuildAdjustmentMutation(null!, delta: 1);

        act.Should().Throw<ArgumentNullException>();
    }
}
