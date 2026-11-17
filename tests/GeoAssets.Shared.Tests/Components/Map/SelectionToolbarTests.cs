using System.Text.Json;
using FluentAssertions;
using GeoAssets.Shared.Components.Map;
using GeoAssets.Shared.Interfaces;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Map;

/// <summary>
/// Covers <see cref="SelectionToolbar"/>'s pure decisions (this repo has no bUnit), and that every
/// localization key the toolbar can ask for exists in all three language files (XD01-310).
/// </summary>
public class SelectionToolbarTests
{
    [Theory]
    [InlineData(MapSelectionTool.None, MapSelectionTool.Box, MapSelectionTool.Box)]
    [InlineData(MapSelectionTool.Box, MapSelectionTool.Fence, MapSelectionTool.Fence)]
    [InlineData(MapSelectionTool.ClickToggle, MapSelectionTool.Box, MapSelectionTool.Box)]
    [InlineData(MapSelectionTool.Box, MapSelectionTool.Box, MapSelectionTool.None)]
    [InlineData(MapSelectionTool.ClickToggle, MapSelectionTool.ClickToggle, MapSelectionTool.None)]
    [InlineData(MapSelectionTool.Fence, MapSelectionTool.Fence, MapSelectionTool.None)]
    public void NextTool_ClickingThePressedToolDisarms_ClickingAnotherArmsIt(MapSelectionTool current, MapSelectionTool clicked, MapSelectionTool expected) =>
        SelectionToolbar.NextTool(current, clicked).Should().Be(expected);

    [Fact]
    public void ToolOptions_AreClickBoxFence_InThatOrder() =>
        SelectionToolbar.ToolOptions.Select(o => o.Tool).Should().Equal(MapSelectionTool.ClickToggle, MapSelectionTool.Box, MapSelectionTool.Fence);

    [Theory]
    [InlineData(-1, "map.select.count.none")]
    [InlineData(0, "map.select.count.none")]
    [InlineData(1, "map.select.count.one")]
    [InlineData(2, "map.select.count.many")]
    [InlineData(10_000, "map.select.count.many")]
    public void CountKey_PicksTheNoneOneOrManyForm(int count, string expected) =>
        SelectionToolbar.CountKey(count).Should().Be(expected);

    [Theory]
    [InlineData(MapSelectionNoticeKind.Overflow, "map.select.notice.overflow")]
    [InlineData(MapSelectionNoticeKind.TooLarge, "map.select.notice.tooLarge")]
    [InlineData(MapSelectionNoticeKind.InvalidArea, "map.select.notice.invalidArea")]
    [InlineData(MapSelectionNoticeKind.Unsupported, "map.select.notice.unsupported")]
    [InlineData(MapSelectionNoticeKind.Failed, "map.select.notice.failed")]
    [InlineData((MapSelectionNoticeKind)99, "map.select.notice.failed")]
    public void NoticeKey_MapsEveryKind_AndFallsBackToTheGenericFailure(MapSelectionNoticeKind kind, string expected) =>
        SelectionToolbar.NoticeKey(kind).Should().Be(expected);

    [Fact]
    public void Notice_OnlyOverflowIsNotAnError()
    {
        new MapSelectionNotice(MapSelectionNoticeKind.Overflow, 3, 10).IsError.Should().BeFalse();
        Enum.GetValues<MapSelectionNoticeKind>().Where(k => k != MapSelectionNoticeKind.Overflow)
            .Should().OnlyContain(k => new MapSelectionNotice(k, 0, 0).IsError);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(1, false, true)]
    [InlineData(1, true, false)]
    [InlineData(10_000, false, true)]
    public void CanCreateOrder_OnceSomethingIsSelected_AndNothingIsResolving(int count, bool busy, bool expected) =>
        SelectionToolbar.CanCreateOrder(count, busy).Should().Be(expected);

    // ── i18n ─────────────────────────────────────────────────────────────────

    private static readonly string[] _staticKeys =
    [
        "map.select.toolbar", "map.select.label", "map.select.tools", "map.select.clear",
        "map.select.clear.title", "map.select.resolving", "map.select.notice.dismiss",
        "map.select.createOrder", "map.select.createOrder.title"
    ];

    private static IEnumerable<string> ToolbarKeys() =>
        _staticKeys
            .Concat(SelectionToolbar.ToolOptions.SelectMany(o => new[] { o.LabelKey, o.TitleKey }))
            .Concat(new[] { 0, 1, 2 }.Select(SelectionToolbar.CountKey))
            .Concat(Enum.GetValues<MapSelectionNoticeKind>().Select(SelectionToolbar.NoticeKey));

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
    public void EveryKeyTheToolbarUses_ExistsWithANonBlankValue(string language)
    {
        var strings = LoadLanguage(language);

        foreach (var key in ToolbarKeys())
            strings.Should().ContainKey(key).WhoseValue.Should().NotBeNullOrWhiteSpace($"'{key}' must be translated in {language}.json");
    }

    [Fact]
    public void SelectionKeys_AreTheSameSetInAllThreeLanguages()
    {
        static string[] SelectionKeys(Dictionary<string, string> strings) =>
            [.. strings.Keys.Where(k => k.StartsWith("map.select.", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

        var english = SelectionKeys(LoadLanguage("en"));

        english.Should().BeEquivalentTo(ToolbarKeys().Distinct(StringComparer.Ordinal));
        SelectionKeys(LoadLanguage("es")).Should().Equal(english);
        SelectionKeys(LoadLanguage("pt")).Should().Equal(english);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("pt")]
    public void FormattedMessages_KeepTheirPlaceholders(string language)
    {
        var strings = LoadLanguage(language);

        strings["map.select.count.many"].Should().Contain("{0}");
        strings["map.select.notice.overflow"].Should().Contain("{0}").And.Contain("{1}");
        strings["map.select.notice.tooLarge"].Should().Contain("{1}");
    }
}
