using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator.Occupancy;

public sealed class OccupancyValueTypeTests
{
    [Fact]
    public void PoleOccupancyQuery_Defaults_AreTheFirstPageOfFifty_Unfiltered()
    {
        var query = new PoleOccupancyQuery();

        query.Page.Should().Be(1);
        query.PageSize.Should().Be(PoleOccupancyQuery.DefaultPageSize).And.Be(50);
        query.OccupantOrganizationId.Should().BeNull();
        query.Skip.Should().Be(0);
        PoleOccupancyQuery.MaxPageSize.Should().Be(200);
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(3, 200, 400)]
    [InlineData(2, 50, 50)]
    public void PoleOccupancyQuery_Skip_IsThePagesBefore(int page, int pageSize, int skip)
    {
        new PoleOccupancyQuery(page, pageSize).Skip.Should().Be(skip);
    }

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 201)]
    public void PoleOccupancyQuery_InvalidPaging_IsRejectedOnConstructionAndOnWith(int page, int pageSize)
    {
        var construct = () => new PoleOccupancyQuery(page, pageSize);
        var change = () => new PoleOccupancyQuery() with { Page = page, PageSize = pageSize };

        construct.Should().Throw<ArgumentOutOfRangeException>();
        change.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PoleOccupancyQuery_KeepsTheOccupantFilter()
    {
        var occupant = Guid.NewGuid();

        new PoleOccupancyQuery(occupantOrganizationId: occupant).OccupantOrganizationId.Should().Be(occupant);
    }

    [Fact]
    public void OccupancyConflictException_CarriesACopyOfThePoles_AndNamesThemInTheMessage()
    {
        var poles = new List<string> { "pole-1", "pole-2" };

        var exception = new OccupancyConflictException(poles);
        poles.Clear();

        exception.PoleFeatureIds.Should().Equal("pole-1", "pole-2");
        exception.Message.Should().Contain("'pole-1', 'pole-2'");
        exception.Should().BeAssignableTo<InvalidOperationException>();
    }

    [Fact]
    public void OccupancyConflictException_StandardConstructors_LeaveThePolesEmpty()
    {
        var inner = new InvalidOperationException("inner");

        new OccupancyConflictException().PoleFeatureIds.Should().BeEmpty();
        new OccupancyConflictException("message").Message.Should().Be("message");
        new OccupancyConflictException("message", inner).InnerException.Should().BeSameAs(inner);
        var nullPoles = () => new OccupancyConflictException((IReadOnlyCollection<string>)null!);
        nullPoles.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PoleOccupantPresence_IsAValue()
    {
        var organization = Guid.NewGuid();
        IReadOnlyList<string> poles = ["pole-1"];

        new PoleOccupantPresence(organization, poles).Should().Be(new PoleOccupantPresence(organization, poles));
    }
}
