using FluentAssertions;
using GeoAssets.Shared.Services;
using Xunit;

namespace GeoAssets.Shared.Tests.Services;

/// <summary>XD01-435: the MapLibre mode's settings — where its style is, and the refresh intervals.</summary>
public sealed class MapLibreOptionsTests
{
    [Fact]
    public void Defaults_point_at_the_kernel_style_and_refresh_every_minute()
    {
        var options = new MapLibreOptions();

        options.StyleUrl.Should().Be("tiles/style.json");
        options.StyleRefreshInterval.Should().Be(TimeSpan.FromMinutes(1));
        options.WriteRefreshDelay.Should().Be(TimeSpan.FromSeconds(1.5));
        new MapInteropOptions().MapLibre.Should().NotBeNull();
        new MapInteropOptions().RenderMode.Should().Be(MapRenderMode.Leaflet, "Leaflet stays the default (MAUI)");
    }

    [Theory]
    [InlineData("tiles/style.json", "https://gw.example/", "https://gw.example/tiles/style.json")]
    [InlineData("tiles/style.json", "https://gw.example", "https://gw.example/tiles/style.json")]
    [InlineData("tiles/style.json", "https://gw.example/geo/", "https://gw.example/geo/tiles/style.json")]
    [InlineData("tiles/style.json", "https://gw.example/geo", "https://gw.example/geo/tiles/style.json")]
    [InlineData("/tiles/style.json?layers=assets", "https://gw.example/geo/", "https://gw.example/geo/tiles/style.json?layers=assets")]
    [InlineData(null, "https://gw.example/", "https://gw.example/tiles/style.json")]
    [InlineData("  ", "https://gw.example/", "https://gw.example/tiles/style.json")]
    [InlineData("https://tiles.example/custom/style.json", "https://gw.example/", "https://tiles.example/custom/style.json")]
    public void A_relative_style_is_under_the_gateway_and_an_absolute_one_is_kept(string? styleUrl, string gateway, string expected)
    {
        MapLibreOptions.ResolveStyleUrl(styleUrl, new Uri(gateway)).Should().Be(expected);
    }

    [Theory]
    [InlineData("tiles/style.json", "tiles/style.json")]
    [InlineData(null, "tiles/style.json")]
    [InlineData("https://gw.example/tiles/style.json", "https://gw.example/tiles/style.json")]
    public void Without_a_gateway_a_relative_style_stays_relative_for_the_page_to_resolve(string? styleUrl, string expected)
    {
        MapLibreOptions.ResolveStyleUrl(styleUrl, null).Should().Be(expected);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/style.json")]
    [InlineData("//evil.example/style.json")]
    public void Another_scheme_stays_a_path_under_the_gateway(string styleUrl)
    {
        var resolved = new Uri(MapLibreOptions.ResolveStyleUrl(styleUrl, new Uri("https://gw.example/geo/")));

        resolved.Scheme.Should().Be("https");
        resolved.Host.Should().Be("gw.example");
        resolved.AbsolutePath.Should().StartWith("/geo/");
    }

    [Fact]
    public void EnsureValid_accepts_zero_and_refuses_negative_intervals()
    {
        MapLibreOptions.EnsureValid(new MapLibreOptions { StyleRefreshInterval = TimeSpan.Zero, WriteRefreshDelay = TimeSpan.Zero });

        FluentActions.Invoking(() => MapLibreOptions.EnsureValid(new MapLibreOptions { StyleRefreshInterval = TimeSpan.FromSeconds(-1) }))
            .Should().Throw<ArgumentOutOfRangeException>().WithMessage("*StyleRefreshInterval*");
        FluentActions.Invoking(() => MapLibreOptions.EnsureValid(new MapLibreOptions { WriteRefreshDelay = TimeSpan.FromSeconds(-1) }))
            .Should().Throw<ArgumentOutOfRangeException>().WithMessage("*WriteRefreshDelay*");
        FluentActions.Invoking(() => MapLibreOptions.EnsureValid(null!)).Should().Throw<ArgumentNullException>();
    }
}
