using FluentAssertions;
using GeoAssets.IpBilling.AssetTypes;
using Xunit;

namespace GeoAssets.IpBilling.Tests;

public class IpBillingAssetTypesTests
{
    [Fact]
    public void Defaults_HasExactlyThreeTypes()
    {
        IpBillingAssetTypes.Defaults.Should().HaveCount(3)
            .And.Contain(IpBillingAssetTypes.Transformador)
            .And.Contain(IpBillingAssetTypes.Poste)
            .And.Contain(IpBillingAssetTypes.LampGroup24h);
    }

    [Fact]
    public void Defaults_AllHaveDistinctWellKnownIds()
    {
        var ids = IpBillingAssetTypes.Defaults.Select(t => t.Id).ToList();

        ids.Should().OnlyHaveUniqueItems();
        ids.Should().NotContain(Core.Models.AssetType.Defaults.Select(t => t.Id));
    }

    [Fact]
    public void Defaults_AreAllGlobalBuiltInAndProtected()
    {
        foreach (var type in IpBillingAssetTypes.Defaults)
        {
            type.IsBuiltIn.Should().BeTrue();
            type.IsProtected.Should().BeTrue();
            type.OrganizationId.Should().Be(Guid.Empty);
        }
    }
}
