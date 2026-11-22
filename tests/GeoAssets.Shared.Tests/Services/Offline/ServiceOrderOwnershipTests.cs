using FluentAssertions;
using GeoAssets.Shared.Services.Offline;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Offline;

/// <summary><see cref="ServiceOrderOwnership.OtherOwnerOf"/> (XD01-328): which pulled orders name another organization as owner.</summary>
public sealed class ServiceOrderOwnershipTests
{
    private static readonly Guid Bimbo = new("b1b0b1b0-0000-4000-8000-000000000328");
    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000328");

    private static ServiceOrder Order(Guid organizationId) =>
        new() { Id = "o-1", Title = "o-1", OrderTypeId = "cut", CreatedBy = "seed", OrganizationId = organizationId };

    [Fact]
    public void AnotherOrganizationsOrder_NamesItsOwner() =>
        ServiceOrderOwnership.OtherOwnerOf(Order(Coelba), Bimbo).Should().Be(Coelba);

    [Fact]
    public void TheTechniciansOwnOrder_NamesNoOwner() =>
        ServiceOrderOwnership.OtherOwnerOf(Order(Bimbo), Bimbo).Should().BeNull();

    [Fact]
    public void AnOrderOfNoOrganization_NamesNoOwner() =>
        ServiceOrderOwnership.OtherOwnerOf(Order(Guid.Empty), Bimbo).Should().BeNull();

    [Fact]
    public void ATechnicianWithoutAnOrganization_SeesEveryOwnedOrderAsAnothersOrganizations() =>
        ServiceOrderOwnership.OtherOwnerOf(Order(Coelba), Guid.Empty).Should().Be(Coelba);

    [Fact]
    public void ANullOrder_Throws()
    {
        var act = () => ServiceOrderOwnership.OtherOwnerOf(null!, Bimbo);

        act.Should().Throw<ArgumentNullException>();
    }
}
