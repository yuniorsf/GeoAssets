using FluentAssertions;
using GeoAssets.Core.Providers;
using Xunit;

namespace GeoAssets.Core.Tests.Providers;

public sealed class FilteringOrganizationCatalogFactoryTests
{
    private static readonly Guid OrgA = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid OrgB = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002");

    private readonly TestAssetProvider _catalog = new();

    [Fact]
    public void GetForOrganization_SameOrganization_ReturnsTheSameInstance()
    {
        var sut = new FilteringOrganizationCatalogFactory(_catalog);

        sut.GetForOrganization(OrgA).Should().BeSameAs(sut.GetForOrganization(OrgA));
    }

    [Fact]
    public void GetForOrganization_DifferentOrganizations_ReturnDifferentViews()
    {
        var sut = new FilteringOrganizationCatalogFactory(_catalog);

        sut.GetForOrganization(OrgA).Should().NotBeSameAs(sut.GetForOrganization(OrgB));
    }

    [Theory]
    [MemberData(nameof(Organizations))]
    public void GetForOrganization_ReturnsAFilteringViewForThatOrganizationOverTheCatalog(Guid organizationId)
    {
        var sut = new FilteringOrganizationCatalogFactory(_catalog);

        var view = sut.GetForOrganization(organizationId);

        var filtered = view.Should().BeOfType<OrganizationFilteredAssetProvider>().Subject;
        filtered.OrganizationId.Should().Be(organizationId);
        view.UnwrapToConcrete().Should().BeSameAs(_catalog);
    }

    [Theory]
    [MemberData(nameof(Organizations))]
    public async Task GetForOrganizationAsync_ReturnsTheSameViewAsTheSyncOne(Guid organizationId)
    {
        var sut = new FilteringOrganizationCatalogFactory(_catalog);

        var asyncView = await sut.GetForOrganizationAsync(organizationId, CancellationToken.None);

        asyncView.Should().BeSameAs(sut.GetForOrganization(organizationId));
    }

    public static TheoryData<Guid> Organizations() => new() { OrgA, OrgB, Guid.Empty };
}
