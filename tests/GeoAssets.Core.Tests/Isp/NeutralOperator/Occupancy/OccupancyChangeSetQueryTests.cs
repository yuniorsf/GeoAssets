using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using Xunit;

namespace GeoAssets.Core.Tests.Isp.NeutralOperator.Occupancy;

public sealed class OccupancyChangeSetQueryTests
{
    [Fact]
    public void Defaults_AreTheFirstPageOfFifty_InEveryStatus()
    {
        var query = new OccupancyChangeSetQuery("ord-1");

        query.Should().BeEquivalentTo(new { OrderId = "ord-1", Page = 1, PageSize = 50, Status = (OccupancyChangeSetStatus?)null, Skip = 0 });
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(3, 200, 400)]
    [InlineData(7, 25, 150)]
    public void Skip_IsTheNumberOfSetsBeforeThePage(int page, int pageSize, int expected) =>
        new OccupancyChangeSetQuery("ord-1", page, pageSize).Skip.Should().Be(expected);

    [Theory]
    [InlineData(0, 50)]
    [InlineData(-1, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 201)]
    public void PagingOutOfRange_IsRejected_OnConstructionAndOnWith(int page, int pageSize)
    {
        ((Action)(() => _ = new OccupancyChangeSetQuery("ord-1", page, pageSize))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => _ = new OccupancyChangeSetQuery("ord-1") with { Page = page, PageSize = pageSize })).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void BlankOrder_AndUndefinedStatus_AreRejected()
    {
        ((Action)(() => _ = new OccupancyChangeSetQuery(" "))).Should().Throw<ArgumentException>();
        ((Action)(() => _ = new OccupancyChangeSetQuery("ord-1", status: (OccupancyChangeSetStatus)9))).Should().Throw<ArgumentOutOfRangeException>();
        new OccupancyChangeSetQuery("ord-1", status: OccupancyChangeSetStatus.Discarded).Status.Should().Be(OccupancyChangeSetStatus.Discarded);
    }
}
