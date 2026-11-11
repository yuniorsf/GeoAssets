using FluentAssertions;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Isp.NeutralOperator;

public class PoleCensusSlotOccupancyMutationFactoryTests
{
    private static GeoFeature PoleFeature() => new() { Id = "pole-1", Properties = new() { AssetTypeId = "neutral-pole" } };

    private static Dictionary<string, string> ConformantAttributes() => new(StringComparer.Ordinal)
    {
        ["structuralCondition"] = "Fair",
        ["band500mmCompliant"] = "true",
        ["clearanceMeasurements"] = """{"groundToLowestCableMeters": 4.8, "powerToCommunicationBandMeters": 0.55}""",
        ["occupants"] = """[{"slotIndex": 1, "status": "AuthorizedContracted", "operatorCnpj": "12345678000190"}]""",
        ["unauthorizedCableDetected"] = "true",
    };

    private static ServiceOrder OrderTargeting(GeoFeature pole, Dictionary<string, string> attributes) => new()
    {
        OrderTypeId = PoleCensusOrderTypes.PoleCensusInspectionId,
        Features = [pole],
        Attributes = attributes,
    };

    [Fact]
    public void BuildMutation_ProjectsScalarFieldsAndCopiesOccupantsOntoCustomAttributes()
    {
        var pole = PoleFeature();
        var order = OrderTargeting(pole, ConformantAttributes());

        var mutation = PoleCensusSlotOccupancyMutationFactory.BuildMutation(order);

        mutation.FeatureId.Should().Be("pole-1");
        mutation.MutationKind.Should().Be(AssetMutationKind.UpdateAttributes);
        mutation.AssetTypeId.Should().Be("neutral-pole");
        mutation.CustomAttributes["structuralCondition"].Should().Be("Fair");
        mutation.CustomAttributes["band500mmCompliant"].Should().Be("true");
        mutation.CustomAttributes["occupants"].Should().Contain("AuthorizedContracted");
        mutation.Geometry.Should().BeNull();
        mutation.TopologyEdges.Should().BeEmpty();
    }

    [Fact]
    public void BuildMutation_OrderWithoutExactlyOneFeature_Throws()
    {
        var order = OrderTargeting(PoleFeature(), ConformantAttributes());
        order.Features.Add(PoleFeature());

        var act = () => PoleCensusSlotOccupancyMutationFactory.BuildMutation(order);

        act.Should().Throw<InvalidOperationException>();
    }
}
