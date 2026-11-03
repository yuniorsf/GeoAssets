using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator;

public class TowerAttachmentCatalogTopologyTests
{
    [Fact]
    public void GetAttachedEquipmentFeatures_ReturnsOnlySupportsNeighbors()
    {
        var tower = new GeoFeature
        {
            Id = "tower-1",
            Geometry = new GeoPoint(-46.63, -23.55),
            Properties = new GeoFeatureProperties { Name = "Tower 1" },
            Topology =
            [
                new TopoEdge { TargetId = "dish-a", Kind = PoleAttachmentSlotMatrix.SupportsEdgeKind },
                new TopoEdge { TargetId = "radome-b", Kind = PoleAttachmentSlotMatrix.SupportsEdgeKind },
                new TopoEdge { TargetId = "other-1", Kind = "unrelated" },
            ],
        };
        var dishA = new GeoFeature { Id = "dish-a", Geometry = new GeoPoint(-46.631, -23.551), Properties = new GeoFeatureProperties { Name = "Dish A" } };
        var radomeB = new GeoFeature { Id = "radome-b", Geometry = new GeoPoint(-46.632, -23.552), Properties = new GeoFeatureProperties { Name = "Radome B" } };
        var other = new GeoFeature { Id = "other-1", Geometry = new GeoPoint(-46.633, -23.553), Properties = new GeoFeatureProperties { Name = "Other" } };

        var catalog = new TowerAttachmentCatalog { TowerFeatureId = "tower-1" };
        var attached = catalog.GetAttachedEquipmentFeatures([tower, dishA, radomeB, other]);

        attached.Should().BeEquivalentTo([dishA, radomeB]);
    }

    [Fact]
    public void GetAttachedEquipmentFeatures_NoSupportsEdges_ReturnsEmpty()
    {
        var tower = new GeoFeature { Id = "tower-1", Geometry = new GeoPoint(-46.63, -23.55), Properties = new GeoFeatureProperties { Name = "Tower 1" } };

        var catalog = new TowerAttachmentCatalog { TowerFeatureId = "tower-1" };
        var attached = catalog.GetAttachedEquipmentFeatures([tower]);

        attached.Should().BeEmpty();
    }

    [Fact]
    public void SupportsEdgeKind_IsSharedWithPoleAttachmentSlotMatrix()
    {
        // The AC requires pole and tower attachment records to share the same multi-tenant
        // query/report surface — this is the concrete mechanism: both aggregates key their
        // topology reads off the exact same TopoEdge.Kind string constant, so any report
        // filtering on "supports" edges picks up both uniformly.
        PoleAttachmentSlotMatrix.SupportsEdgeKind.Should().Be("supports");
    }
}
