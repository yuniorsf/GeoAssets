using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Workflow.Agents.Dispatch;
using GeoAssets.Workflow.Dispatch;
using Xunit;

namespace GeoAssets.Workflow.Agents.Tests.Dispatch;

public class FeederClusteringServiceTests
{
    [Fact]
    public void Cluster_TwoOrdersUnderTheSameFeeder_GroupTogether()
    {
        var provider = new TestAssetProvider();
        provider.Add(new GeoFeature
        {
            Id = "feeder-1",
            Geometry = new GeoPoint(-46.0, -23.0),
            Properties = new GeoFeatureProperties { AssetTypeId = FeederClusterDispatchTypes.FeederAssetTypeId, Name = "feeder-1" },
            Topology =
            [
                new TopoEdge { TargetId = "pole-a", Kind = "feeds" },
                new TopoEdge { TargetId = "pole-b", Kind = "feeds" },
            ],
        });
        provider.Add(new GeoFeature { Id = "pole-a", Geometry = new GeoPoint(-46.01, -23.01), Properties = new GeoFeatureProperties { AssetTypeId = "pole" } });
        provider.Add(new GeoFeature { Id = "pole-b", Geometry = new GeoPoint(-46.02, -23.02), Properties = new GeoFeatureProperties { AssetTypeId = "pole" } });

        var orders = new List<ServiceOrderWithLocation>
        {
            new("order-a", "pole-a", new GeoPoint(-46.01, -23.01)),
            new("order-b", "pole-b", new GeoPoint(-46.02, -23.02)),
        };

        var clusters = FeederClusteringService.Cluster(orders, provider);

        clusters.Should().ContainKey("feeder-1");
        clusters["feeder-1"].Select(o => o.OrderId).Should().BeEquivalentTo("order-a", "order-b");
    }

    [Fact]
    public void Cluster_FeatureWithNoFeederAncestor_FallsBackToItsOwnFeatureIdAsCluster()
    {
        var provider = new TestAssetProvider();
        provider.Add(new GeoFeature { Id = "orphan-pole", Geometry = new GeoPoint(-46.5, -23.5), Properties = new GeoFeatureProperties { AssetTypeId = "pole" } });

        var orders = new List<ServiceOrderWithLocation> { new("order-x", "orphan-pole", new GeoPoint(-46.5, -23.5)) };

        var clusters = FeederClusteringService.Cluster(orders, provider);

        clusters.Should().ContainKey("orphan-pole");
        clusters["orphan-pole"].Should().ContainSingle(o => o.OrderId == "order-x");
    }

    [Fact]
    public void Cluster_TwoFeedersWithDifferentDownstreamOrders_ProduceSeparateClusters()
    {
        var provider = new TestAssetProvider();
        provider.Add(new GeoFeature
        {
            Id = "feeder-1",
            Geometry = new GeoPoint(-46.0, -23.0),
            Properties = new GeoFeatureProperties { AssetTypeId = FeederClusterDispatchTypes.FeederAssetTypeId },
            Topology = [new TopoEdge { TargetId = "pole-a", Kind = "feeds" }],
        });
        provider.Add(new GeoFeature
        {
            Id = "feeder-2",
            Geometry = new GeoPoint(-47.0, -24.0),
            Properties = new GeoFeatureProperties { AssetTypeId = FeederClusterDispatchTypes.FeederAssetTypeId },
            Topology = [new TopoEdge { TargetId = "pole-c", Kind = "feeds" }],
        });
        provider.Add(new GeoFeature { Id = "pole-a", Geometry = new GeoPoint(-46.01, -23.01), Properties = new GeoFeatureProperties { AssetTypeId = "pole" } });
        provider.Add(new GeoFeature { Id = "pole-c", Geometry = new GeoPoint(-47.01, -24.01), Properties = new GeoFeatureProperties { AssetTypeId = "pole" } });

        var orders = new List<ServiceOrderWithLocation>
        {
            new("order-a", "pole-a", new GeoPoint(-46.01, -23.01)),
            new("order-c", "pole-c", new GeoPoint(-47.01, -24.01)),
        };

        var clusters = FeederClusteringService.Cluster(orders, provider);

        clusters.Should().HaveCount(2);
        clusters["feeder-1"].Should().ContainSingle(o => o.OrderId == "order-a");
        clusters["feeder-2"].Should().ContainSingle(o => o.OrderId == "order-c");
    }
}
