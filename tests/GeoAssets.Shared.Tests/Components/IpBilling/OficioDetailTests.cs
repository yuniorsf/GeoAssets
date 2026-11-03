using GeoAssets.IpBilling.Oficio;
using GeoAssets.Shared.Components.IpBilling;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.IpBilling;

/// <summary>
/// No bUnit-style component-rendering test convention exists yet in this project, so this
/// exercises <see cref="OficioDetail.IsLineSwapped"/> directly — the pure lookup that decides
/// what <c>[Parameter] bool IsSwap</c> value <c>OficioSwapBadge</c> (XD01-187) is given for a
/// line, which is what "swap badge visible and correct on swapped lines" actually depends on.
/// </summary>
public class OficioDetailTests
{
    [Fact]
    public void IsLineSwapped_LineFlaggedAsSwap_ReturnsTrue()
    {
        var lineId = Guid.NewGuid();
        var swaps = new List<OficioLineSwapResult>
        {
            new(lineId, "INST-1", IsSwap: true),
        };

        Assert.True(OficioDetail.IsLineSwapped(swaps, lineId));
    }

    [Fact]
    public void IsLineSwapped_LineNotFlaggedAsSwap_ReturnsFalse()
    {
        var lineId = Guid.NewGuid();
        var swaps = new List<OficioLineSwapResult>
        {
            new(lineId, "INST-1", IsSwap: false),
        };

        Assert.False(OficioDetail.IsLineSwapped(swaps, lineId));
    }

    [Fact]
    public void IsLineSwapped_LineNotPresentInSwapsList_ReturnsFalse()
    {
        var lineId = Guid.NewGuid();
        var otherLineId = Guid.NewGuid();
        var swaps = new List<OficioLineSwapResult>
        {
            new(otherLineId, "INST-2", IsSwap: true),
        };

        Assert.False(OficioDetail.IsLineSwapped(swaps, lineId));
    }

    [Fact]
    public void IsLineSwapped_EmptySwapsList_ReturnsFalse()
    {
        Assert.False(OficioDetail.IsLineSwapped([], Guid.NewGuid()));
    }
}
