using FluentAssertions;
using GeoAssets.Shared.Interfaces;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.MapTiles;

/// <summary>XD01-96: the three answers the map understands, and that none of them prints its token.</summary>
public sealed class MapTileAccessTests
{
    private static readonly DateTimeOffset Expiry = new(2099, 1, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Anonymous_RequiresNoToken()
    {
        MapTileAccess.Anonymous.Should().Be(new MapTileAccess(false, null, null));
    }

    [Fact]
    public void Unavailable_RequiresATokenItDoesNotHave()
    {
        MapTileAccess.Unavailable.Should().Be(new MapTileAccess(true, null, null));
    }

    [Fact]
    public void Bearer_CarriesTheTokenAndItsExpiry()
    {
        MapTileAccess.Bearer("token-1", Expiry).Should().Be(new MapTileAccess(true, "token-1", Expiry));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Bearer_RefusesABlankToken(string? token)
    {
        var act = () => MapTileAccess.Bearer(token!, Expiry);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToString_NeverPrintsTheToken()
    {
        var text = MapTileAccess.Bearer("secret-token-value", Expiry).ToString();

        text.Should().NotContain("secret-token-value");
        text.Should().Contain("AccessToken = ***").And.Contain("RequiresToken = True").And.Contain("2099-01-01");
        MapTileAccess.Anonymous.ToString().Should().Contain("AccessToken = null").And.Contain("ExpiresOn = null");
    }
}
