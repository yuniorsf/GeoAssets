using FluentAssertions;
using GeoAssets.Shared.Components.IpBilling;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.IpBilling;

/// <summary>
/// The pure paging math behind <see cref="IpBillingPager"/> (XD01-301) — exercised directly,
/// same convention as <c>AssetsTableTests</c> (no component-rendering test infrastructure).
/// </summary>
public class IpBillingPagerTests
{
    [Theory]
    [InlineData(0, 25, 0)]
    [InlineData(-3, 25, 0)]
    [InlineData(1, 25, 1)]
    [InlineData(25, 25, 1)]
    [InlineData(26, 25, 2)]
    [InlineData(100, 25, 4)]
    [InlineData(101, 25, 5)]
    [InlineData(10, 0, 0)]
    [InlineData(10, -1, 0)]
    public void ComputeTotalPages_RoundsUp_AndIsZeroWithoutRowsOrPageSize(int totalCount, int pageSize, int expected) =>
        IpBillingPager.ComputeTotalPages(totalCount, pageSize).Should().Be(expected);

    [Theory]
    [InlineData(1, 0, 25, 1)]   // an empty table still shows "page 1"
    [InlineData(3, 0, 25, 1)]
    [InlineData(0, 50, 25, 1)]  // below the first page
    [InlineData(-4, 50, 25, 1)]
    [InlineData(2, 50, 25, 2)]
    [InlineData(3, 50, 25, 2)]  // the last row of page 3 was deleted → step back to page 2
    [InlineData(9, 51, 25, 3)]
    public void ClampPage_KeepsThePageWithinTheAvailableRange(int page, int totalCount, int pageSize, int expected) =>
        IpBillingPager.ClampPage(page, totalCount, pageSize).Should().Be(expected);
}
