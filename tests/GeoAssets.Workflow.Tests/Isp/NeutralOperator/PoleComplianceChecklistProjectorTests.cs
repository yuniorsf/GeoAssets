using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Isp.NeutralOperator;

public class PoleComplianceChecklistProjectorTests
{
    private static GeoFeature PoleFeature(string id = "pole-1") => new() { Id = id };

    private static ServiceOrder OrderTargeting(GeoFeature pole, Dictionary<string, string> attributes) => new()
    {
        OrderTypeId = PoleCensusOrderTypes.PoleCensusInspectionId,
        Features = [pole],
        Attributes = attributes,
    };

    private static Dictionary<string, string> ConformantAttributes() => new(StringComparer.Ordinal)
    {
        ["structuralCondition"] = "Fair",
        ["band500mmCompliant"] = "true",
        ["clearanceMeasurements"] = """{"groundToLowestCableMeters": 4.8, "powerToCommunicationBandMeters": 0.55}""",
        ["occupants"] = """
            [
              {"slotIndex": 1, "status": "AuthorizedContracted", "operatorCnpj": "12345678000190", "cableLoadDaN": 350.5},
              {"slotIndex": 2, "status": "ClandestineUnauthorized"}
            ]
            """,
        ["unauthorizedCableDetected"] = "true",
    };

    [Fact]
    public void Project_WritesScalarFieldsOntoTheTargetFeature()
    {
        var pole = PoleFeature();
        var order = OrderTargeting(pole, ConformantAttributes());

        PoleComplianceChecklistProjector.Project(order);

        pole.Properties.CustomAttributes["structuralCondition"].Should().Be("Fair");
        pole.Properties.CustomAttributes["band500mmCompliant"].Should().Be("true");
        pole.Properties.CustomAttributes["clearanceMeasurements"].Should().Contain("4.8");
        pole.Properties.CustomAttributes["unauthorizedCableDetected"].Should().Be("true");
    }

    [Fact]
    public void Project_BuildsAttachmentSlotMatrixFromOccupantsArray()
    {
        var pole = PoleFeature("pole-42");
        var order = OrderTargeting(pole, ConformantAttributes());

        var matrix = PoleComplianceChecklistProjector.Project(order);

        matrix.PoleFeatureId.Should().Be("pole-42");
        matrix.Slots.Should().HaveCount(2);

        var authorized = matrix.Slots.Single(s => s.SlotIndex == 1);
        authorized.Status.Should().Be(PoleAttachmentOccupantStatus.AuthorizedContracted);
        authorized.OperatorCnpj.Should().Be("12345678000190");
        authorized.CableLoadDaN.Should().Be(350.5m);

        var clandestine = matrix.Slots.Single(s => s.SlotIndex == 2);
        clandestine.Status.Should().Be(PoleAttachmentOccupantStatus.ClandestineUnauthorized);
        clandestine.OperatorCnpj.Should().BeNull();
        clandestine.CableLoadDaN.Should().Be(0m, "an occupant submitted with no cableLoadDaN hasn't had its load measured yet");
    }

    [Fact]
    public void Project_EmptyOccupantsArray_ReturnsEmptyMatrix()
    {
        var pole = PoleFeature();
        var attributes = ConformantAttributes();
        attributes["occupants"] = "[]";
        var order = OrderTargeting(pole, attributes);

        var matrix = PoleComplianceChecklistProjector.Project(order);

        matrix.Slots.Should().BeEmpty();
    }

    [Fact]
    public void Project_NoTargetFeature_Throws()
    {
        var order = new ServiceOrder
        {
            OrderTypeId = PoleCensusOrderTypes.PoleCensusInspectionId,
            Attributes = ConformantAttributes(),
        };

        var act = () => PoleComplianceChecklistProjector.Project(order);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Project_MultipleTargetFeatures_Throws()
    {
        var order = new ServiceOrder
        {
            OrderTypeId = PoleCensusOrderTypes.PoleCensusInspectionId,
            Features = [PoleFeature("pole-a"), PoleFeature("pole-b")],
            Attributes = ConformantAttributes(),
        };

        var act = () => PoleComplianceChecklistProjector.Project(order);

        act.Should().Throw<InvalidOperationException>();
    }
}
