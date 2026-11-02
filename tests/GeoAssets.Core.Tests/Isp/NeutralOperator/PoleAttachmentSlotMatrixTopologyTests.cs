using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator;

public class PoleAttachmentSlotMatrixTopologyTests
{
    [Fact]
    public void GetAttachedCableFeatures_ReturnsOnlySupportsNeighbors()
    {
        var pole = new GeoFeature
        {
            Id = "pole-1",
            Geometry = new GeoPoint(-46.63, -23.55),
            Properties = new GeoFeatureProperties { Name = "Pole 1" },
            Topology =
            [
                new TopoEdge { TargetId = "cable-a", Kind = PoleAttachmentSlotMatrix.SupportsEdgeKind },
                new TopoEdge { TargetId = "cable-b", Kind = PoleAttachmentSlotMatrix.SupportsEdgeKind },
                new TopoEdge { TargetId = "other-1", Kind = "unrelated" },
            ],
        };
        var cableA = new GeoFeature { Id = "cable-a", Geometry = new GeoPoint(-46.631, -23.551), Properties = new GeoFeatureProperties { Name = "Cable A" } };
        var cableB = new GeoFeature { Id = "cable-b", Geometry = new GeoPoint(-46.632, -23.552), Properties = new GeoFeatureProperties { Name = "Cable B" } };
        var other = new GeoFeature { Id = "other-1", Geometry = new GeoPoint(-46.633, -23.553), Properties = new GeoFeatureProperties { Name = "Other" } };

        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };
        var attached = matrix.GetAttachedCableFeatures([pole, cableA, cableB, other]);

        attached.Should().BeEquivalentTo([cableA, cableB]);
    }

    [Fact]
    public void GetAttachedCableFeatures_NoSupportsEdges_ReturnsEmpty()
    {
        var pole = new GeoFeature { Id = "pole-1", Geometry = new GeoPoint(-46.63, -23.55), Properties = new GeoFeatureProperties { Name = "Pole 1" } };

        var matrix = new PoleAttachmentSlotMatrix { PoleFeatureId = "pole-1" };
        var attached = matrix.GetAttachedCableFeatures([pole]);

        attached.Should().BeEmpty();
    }
}
