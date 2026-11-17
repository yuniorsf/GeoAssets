using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Components.Layers;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Layers;

/// <summary>
/// The pure decisions behind the asset-type editor's zoom-range fields (XD01-311; this repo has
/// no bUnit), and that every localization key they can ask for exists in all three languages.
/// </summary>
public class AssetTypeZoomRangeInputTests
{
    // ── Parsing ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData("", "", null, null)]
    [InlineData("   ", "\t", null, null)]
    [InlineData("13", "15", 13, 15)]
    [InlineData(" 13 ", " 15 ", 13, 15)]
    [InlineData("0", "24", 0, 24)]
    [InlineData("15", "", 15, null)]
    [InlineData("", "13", null, 13)]
    [InlineData("12", "12", 12, 12)]
    [InlineData("+7", null, 7, null)]
    public void Parse_BlankIsAnOpenBound_AndWholeNumbersInRangeAreAccepted(string? minText, string? maxText, int? min, int? max)
    {
        var result = AssetTypeZoomRangeInput.Parse(minText, maxText);

        result.IsValid.Should().BeTrue();
        result.Error.Should().BeNull();
        result.MinZoom.Should().Be(min);
        result.MaxZoom.Should().Be(max);
        result.MinInvalid.Should().BeFalse();
        result.MaxInvalid.Should().BeFalse();
    }

    [Theory]
    [InlineData("abc", "", true, false)]
    [InlineData("", "abc", false, true)]
    [InlineData("7.5", "9", true, false)]
    [InlineData("7,5", "9", true, false)]
    [InlineData("1e1", "", true, false)]
    [InlineData("x", "y", true, true)]
    [InlineData("99999999999", "", true, false)]
    [InlineData("1 2", "", true, false)]
    public void Parse_AnythingButAWholeNumber_IsRejected_AndFlagsTheOffendingField(string minText, string maxText, bool minInvalid, bool maxInvalid)
    {
        var result = AssetTypeZoomRangeInput.Parse(minText, maxText);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(AssetTypeZoomRangeInputError.NotAWholeNumber);
        (result.MinZoom, result.MaxZoom).Should().Be((null, null));
        (result.MinInvalid, result.MaxInvalid).Should().Be((minInvalid, maxInvalid));
    }

    [Theory]
    [InlineData("-1", "", true, false)]
    [InlineData("25", "", true, false)]
    [InlineData("", "-1", false, true)]
    [InlineData("", "25", false, true)]
    [InlineData("3", "100", false, true)]
    public void Parse_ZoomOutsideTheSupportedLevels_IsRejected_AndFlagsTheOffendingField(string minText, string maxText, bool minInvalid, bool maxInvalid)
    {
        var result = AssetTypeZoomRangeInput.Parse(minText, maxText);

        result.Error.Should().Be(AssetTypeZoomRangeInputError.OutOfBounds);
        (result.MinZoom, result.MaxZoom).Should().Be((null, null));
        (result.MinInvalid, result.MaxInvalid).Should().Be((minInvalid, maxInvalid));
    }

    [Theory]
    [InlineData("16", "15")]
    [InlineData("24", "0")]
    public void Parse_MinimumAboveMaximum_IsRejected_AndFlagsBothFields(string minText, string maxText)
    {
        var result = AssetTypeZoomRangeInput.Parse(minText, maxText);

        result.Error.Should().Be(AssetTypeZoomRangeInputError.MinAboveMax);
        (result.MinInvalid, result.MaxInvalid).Should().Be((true, true));
    }

    [Fact]
    public void Parse_DoesNotDependOnTheCurrentCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");

            AssetTypeZoomRangeInput.Parse("13", "15").Should().Be(new AssetTypeZoomRangeInputResult(13, 15, null, false, false));
            AssetTypeZoomRangeInput.Parse("1.000", "").Error.Should().Be(AssetTypeZoomRangeInputError.NotAWholeNumber);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Theory]
    [InlineData("13", "15")]
    [InlineData("", "")]
    [InlineData("-1", "")]
    [InlineData("16", "15")]
    [InlineData("", "25")]
    public void Parse_AgreesWithTheModelsOwnRules(string minText, string maxText)
    {
        int? min = string.IsNullOrEmpty(minText) ? null : int.Parse(minText, CultureInfo.InvariantCulture);
        int? max = string.IsNullOrEmpty(maxText) ? null : int.Parse(maxText, CultureInfo.InvariantCulture);

        AssetTypeZoomRangeInput.Parse(minText, maxText).IsValid
            .Should().Be(AssetTypeZoomRange.FindDefect(min, max) is null);
    }

    // ── Keys ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AssetTypeZoomRangeInputError.NotAWholeNumber, "map.layers.zoomRange.error.notWholeNumber")]
    [InlineData(AssetTypeZoomRangeInputError.OutOfBounds, "map.layers.zoomRange.error.outOfBounds")]
    [InlineData(AssetTypeZoomRangeInputError.MinAboveMax, "map.layers.zoomRange.error.minAboveMax")]
    [InlineData((AssetTypeZoomRangeInputError)99, "map.layers.zoomRange.error.notWholeNumber")]
    public void ErrorKey_MapsEveryError(AssetTypeZoomRangeInputError error, string expected) =>
        AssetTypeZoomRangeInput.ErrorKey(error).Should().Be(expected);

    [Theory]
    [InlineData(null, null, "map.layers.zoomRange.any", new int[0])]
    [InlineData(13, 15, "map.layers.zoomRange.between", new[] { 13, 15 })]
    [InlineData(15, null, "map.layers.zoomRange.from", new[] { 15 })]
    [InlineData(null, 12, "map.layers.zoomRange.upTo", new[] { 12 })]
    public void Summary_PicksTheKeyAndArgumentsForEachShapeOfRange(int? min, int? max, string expectedKey, int[] expectedArguments)
    {
        AssetTypeZoomRangeInput.SummaryKey(min, max).Should().Be(expectedKey);
        AssetTypeZoomRangeInput.SummaryArguments(min, max).Should().Equal(expectedArguments.Cast<object>());
    }

    // ── i18n ─────────────────────────────────────────────────────────────────

    private static readonly string[] _staticKeys =
    [
        "map.layers.zoomRange", "map.layers.zoomRange.min", "map.layers.zoomRange.max", "map.layers.zoomRange.help"
    ];

    private static IEnumerable<string> EditorKeys() =>
        _staticKeys
            .Concat(Enum.GetValues<AssetTypeZoomRangeInputError>().Select(AssetTypeZoomRangeInput.ErrorKey))
            .Concat(new (int?, int?)[] { (null, null), (1, 2), (1, null), (null, 2) }.Select(r => AssetTypeZoomRangeInput.SummaryKey(r.Item1, r.Item2)));

    private static Dictionary<string, string> LoadLanguage(string language)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the test runs from inside the repository");

        var path = Path.Combine(directory!.FullName, "apps", "GeoAssets.Shared", "wwwroot", "i18n", $"{language}.json");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;
    }

    private static string[] ZoomKeys(Dictionary<string, string> strings) =>
        [.. strings.Keys.Where(k => k.StartsWith("map.layers.zoomRange", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("pt")]
    public void EveryKeyTheEditorUses_ExistsWithANonBlankValue(string language)
    {
        var strings = LoadLanguage(language);

        foreach (var key in EditorKeys())
            strings.Should().ContainKey(key).WhoseValue.Should().NotBeNullOrWhiteSpace($"'{key}' in {language}.json");
    }

    [Fact]
    public void ZoomRangeKeys_AreTheSameSetInAllThreeLanguages_AndNoneIsUnused()
    {
        var english = ZoomKeys(LoadLanguage("en"));

        english.Should().BeEquivalentTo(EditorKeys().Distinct(StringComparer.Ordinal));
        ZoomKeys(LoadLanguage("es")).Should().Equal(english);
        ZoomKeys(LoadLanguage("pt")).Should().Equal(english);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("pt")]
    public void FormattedMessages_KeepTheirPlaceholders(string language)
    {
        var strings = LoadLanguage(language);

        strings["map.layers.zoomRange.help"].Should().Contain("{0}").And.Contain("{1}");
        strings["map.layers.zoomRange.error.outOfBounds"].Should().Contain("{0}").And.Contain("{1}");
        strings["map.layers.zoomRange.between"].Should().Contain("{0}").And.Contain("{1}");
        strings["map.layers.zoomRange.from"].Should().Contain("{0}").And.NotContain("{1}");
        strings["map.layers.zoomRange.upTo"].Should().Contain("{0}").And.NotContain("{1}");
        strings["map.layers.zoomRange.any"].Should().NotContain("{0}");
    }
}
