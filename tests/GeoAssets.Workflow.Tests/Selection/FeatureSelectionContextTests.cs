using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Core.Providers;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Selection;
using Xunit;

namespace GeoAssets.Workflow.Tests.Selection;

public class FeatureSelectionContextTests
{
    private static readonly GeoPolygon Area = new([(0, 0), (10, 0), (10, 10), (0, 10), (0, 0)]);

    [Fact]
    public async Task SpatialQuery_NotSet_DefaultsToAnAdapterOverRepository()
    {
        var repo = new TestAssetProvider();
        repo.Add(new GeoFeature { Id = "inside", Geometry = new GeoPoint(5, 5) });
        var context = new FeatureSelectionContext { Repository = repo };

        var result = await context.SpatialQuery.FindCoveredByAsync(
            Area, new AssetSpatialFilter(null, null, int.MaxValue), CancellationToken.None);

        context.SpatialQuery.Should().BeOfType<AssetProviderSpatialQuery>();
        context.SpatialQuery.Should().BeSameAs(context.SpatialQuery, "the default adapter is created once");
        result.Select(f => f.Id).Should().Equal("inside");
    }

    [Fact]
    public void SpatialQuery_Set_OverridesTheDefault()
    {
        var hostQuery = new RecordingSpatialQuery();

        var context = new FeatureSelectionContext { Repository = new TestAssetProvider(), SpatialQuery = hostQuery };

        context.SpatialQuery.Should().BeSameAs(hostQuery);
    }

    [Fact]
    public void SpatialQuery_SetToNull_ThrowsArgumentNullException() =>
        FluentActions.Invoking(() => new FeatureSelectionContext { SpatialQuery = null! })
            .Should().Throw<ArgumentNullException>();

    [Fact]
    public void OrganizationId_DefaultsToNull_AndIsSettable()
    {
        var organizationId = Guid.NewGuid();

        new FeatureSelectionContext().OrganizationId.Should().BeNull();
        new FeatureSelectionContext { OrganizationId = organizationId }.OrganizationId.Should().Be(organizationId);
    }

    [Fact]
    public async Task InterfaceDefaults_AnImplementationThatPredatesTheNewMembers_GetsAnAdapterOverRepositoryAndNoOrganization()
    {
        var repo = new TestAssetProvider();
        repo.Add(new GeoFeature { Id = "inside", Geometry = new GeoPoint(5, 5) });
        IFeatureSelectionContext context = new LegacyContext(repo);

        var result = await context.SpatialQuery.FindCoveredByAsync(
            Area, new AssetSpatialFilter(null, null, int.MaxValue), CancellationToken.None);

        context.SpatialQuery.Should().BeOfType<AssetProviderSpatialQuery>();
        context.OrganizationId.Should().BeNull();
        result.Select(f => f.Id).Should().Equal("inside");
    }

    /// <summary>Implements only the members <see cref="IFeatureSelectionContext"/> had before XD01-307.</summary>
    private sealed class LegacyContext(IAssetProvider repository) : IFeatureSelectionContext
    {
        public IAssetProvider Repository { get; } = repository;
        public IServiceOrder? TargetOrder => null;
        public IServiceOrderRepository? OrderRepository => null;
        public IReadOnlyDictionary<string, object> Parameters { get; } =
            new Dictionary<string, object>(StringComparer.Ordinal);
    }
}
