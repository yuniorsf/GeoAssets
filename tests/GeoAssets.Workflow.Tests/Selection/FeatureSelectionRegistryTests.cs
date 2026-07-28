using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Selection;
using GeoAssets.Workflow.Selection.Strategies;
using Xunit;

namespace GeoAssets.Workflow.Tests.Selection;

public class FeatureSelectionRegistryTests
{
    private static async Task<InMemoryServiceOrderRepository> RepositoryWithParentAsync(params GeoFeature[] features)
    {
        var repo = new InMemoryServiceOrderRepository();
        var parent = new ServiceOrder { Title = "Parent" }.WithFeatures(features);
        await repo.AddAsync(parent);
        return repo;
    }

    // ── SelectAsync parameter validation ──────────────────────────────────────

    [Fact]
    public async Task SelectAsync_SerializableParameters_ReturnsSpec()
    {
        using var registry = new FeatureSelectionRegistry(
            "no-such-plugins-dir", typeof(InheritFromParentOrderStrategy).Assembly);

        var orderRepo = await RepositoryWithParentAsync(new GeoFeature { Id = "f1" });
        var parent = (await orderRepo.GetRootsAsync()).Single();

        var context = new FeatureSelectionContext
        {
            OrderRepository = orderRepo,
            TargetOrder     = new ServiceOrder { ParentOrderId = parent.Id },
            Parameters      = new Dictionary<string, object>(),
        };

        var (features, spec) = await registry.SelectAsync("inherit-parent", context);

        spec.StrategyId.Should().Be("inherit-parent");
        features.Should().ContainSingle();
    }

    [Fact]
    public async Task SelectAsync_NonSerializableParameter_ThrowsInvalidOperationException()
    {
        using var registry = new FeatureSelectionRegistry(
            "no-such-plugins-dir", typeof(InheritFromParentOrderStrategy).Assembly);

        var orderRepo = await RepositoryWithParentAsync(
            new GeoFeature { Id = "f1" }, new GeoFeature { Id = "f2" });
        var parent = (await orderRepo.GetRootsAsync()).Single();

        var context = new FeatureSelectionContext
        {
            OrderRepository = orderRepo,
            TargetOrder     = new ServiceOrder { ParentOrderId = parent.Id },
            Parameters      = new Dictionary<string, object>
            {
                ["filter"] = (Func<GeoFeature, bool>)(f => f.Id == "f1"),
            },
        };

        var act = () => registry.SelectAsync("inherit-parent", context);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithInnerException(typeof(NotSupportedException));
    }
}
