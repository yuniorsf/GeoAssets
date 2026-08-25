using FluentAssertions;
using GeoAssets.Core.Theming;
using GeoAssets.Shared.Theming;
using Xunit;

namespace GeoAssets.Shared.Tests.Theming;

public class BlazorThemeServiceTests
{
    [Theory]
    [InlineData(ThemeMode.Light, true, "light")]
    [InlineData(ThemeMode.Light, false, "light")]
    [InlineData(ThemeMode.Dark, true, "dark")]
    [InlineData(ThemeMode.Dark, false, "dark")]
    [InlineData(ThemeMode.System, true, "dark")]
    [InlineData(ThemeMode.System, false, "light")]
    public void ResolveTheme_ReturnsExpectedPalette(ThemeMode mode, bool systemPrefersDark, string expected) =>
        BlazorThemeService.ResolveTheme(mode, systemPrefersDark).Should().Be(expected);

    [Theory]
    [InlineData("light", ThemeMode.Light)]
    [InlineData("dark", ThemeMode.Dark)]
    [InlineData("system", ThemeMode.System)]
    public void ParseMode_RecognizedValues_ReturnsMatchingMode(string stored, ThemeMode expected) =>
        BlazorThemeService.ParseMode(stored).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("SYSTEM")]
    public void ParseMode_UnrecognizedValues_ReturnsNull(string? stored) =>
        BlazorThemeService.ParseMode(stored).Should().BeNull();
}
