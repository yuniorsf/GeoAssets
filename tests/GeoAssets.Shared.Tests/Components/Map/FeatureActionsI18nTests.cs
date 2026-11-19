using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Map;

/// <summary>The localization keys the map's feature actions use exist in every language file (XD01-370).</summary>
public class FeatureActionsI18nTests
{
    private static readonly string[] Keys =
    [
        "featureActions.loading",
        "featureActions.unavailable",
        "featureActions.createSharedUseOrder",
        "orderTypes.sharedUsePoleWork",
    ];

    private static Dictionary<string, string> LoadLanguage(string language)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the test runs from inside the repository");

        var path = Path.Combine(directory!.FullName, "apps", "GeoAssets.Shared", "wwwroot", "i18n", $"{language}.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("pt")]
    public void FeatureActionKeys_ExistWithANonBlankValue(string language)
    {
        var strings = LoadLanguage(language);

        foreach (var key in Keys)
            strings.Should().ContainKey(key).WhoseValue.Should().NotBeNullOrWhiteSpace($"'{key}' must be translated in {language}.json");
    }
}
