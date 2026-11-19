using System.Text.Json;
using FluentAssertions;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Shared.Services.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Occupancy;

/// <summary>
/// The localization keys of the occupancy editor (XD01-315): every key the editor can show
/// exists, non-blank, in the three language files, and the three files hold the same set.
/// </summary>
public sealed class OccupancyEditorMessagesTests
{
    /// <summary>The keys the two components name literally in their markup and code-behind.</summary>
    private static readonly string[] _staticKeys =
    [
        "occupancy.targets.title", "occupancy.targets.caption", "occupancy.targets.target", "occupancy.targets.open", "occupancy.targets.openFor",
        "occupancy.targets.pager", "occupancy.targets.empty", "occupancy.targets.loadError",
        "occupancy.editor.title", "occupancy.editor.close", "occupancy.editor.loading", "occupancy.editor.readOnly",
        "occupancy.summary.current", "occupancy.summary.preview", "occupancy.summary.occupied",
        "occupancy.badge.overCapacity", "occupancy.badge.unidentified",
        "occupancy.table.caption", "occupancy.table.slot", "occupancy.table.slotNumber", "occupancy.table.beyond", "occupancy.table.status",
        "occupancy.table.occupant", "occupancy.table.cnpj", "occupancy.table.cable", "occupancy.table.cableValue", "occupancy.table.change",
        "occupancy.table.actions", "occupancy.table.free", "occupancy.table.orphan",
        "occupancy.action.add", "occupancy.action.addOn", "occupancy.action.addBeyond", "occupancy.action.modify", "occupancy.action.modifyOn",
        "occupancy.action.remove", "occupancy.action.removeOn", "occupancy.action.markUnidentified", "occupancy.action.markUnidentifiedOn",
        "occupancy.action.undo", "occupancy.action.undoOn", "occupancy.action.save", "occupancy.action.saving", "occupancy.action.reset", "occupancy.action.discard",
        "occupancy.form.title", "occupancy.form.organization", "occupancy.form.organizationHint", "occupancy.form.license", "occupancy.form.diameter",
        "occupancy.form.load", "occupancy.form.cable", "occupancy.form.apply",
        "occupancy.validation.status", "occupancy.validation.reservationBeyondSlots", "occupancy.validation.organization", "occupancy.validation.cnpj",
        "occupancy.validation.license", "occupancy.validation.cable", "occupancy.validation.diameter", "occupancy.validation.load",
    ];

    private static IEnumerable<string> EditorKeys() =>
        _staticKeys
            .Concat(OccupancyEditorMessages.MessageKeys)
            .Concat(Enum.GetValues<PoleAttachmentOccupantStatus>().Select(OccupancyEditorMessages.StatusKey))
            .Concat(Enum.GetValues<OccupancyClass>().Select(OccupancyEditorMessages.ClassKey))
            .Concat(Enum.GetValues<OccupancyChangeKind>().Select(OccupancyEditorMessages.ChangeKindKey));

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
    public void EveryKeyTheEditorUses_ExistsWithANonBlankValue(string language)
    {
        var strings = LoadLanguage(language);

        foreach (var key in EditorKeys())
            strings.Should().ContainKey(key).WhoseValue.Should().NotBeNullOrWhiteSpace($"'{key}' must be translated in {language}.json");
    }

    [Fact]
    public void OccupancyKeys_AreTheSameSetInAllThreeLanguages_AndNoneIsUnused()
    {
        static string[] OccupancyKeys(Dictionary<string, string> strings) =>
            [.. strings.Keys.Where(k => k.StartsWith("occupancy.", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

        var english = OccupancyKeys(LoadLanguage("en"));

        english.Should().BeEquivalentTo(EditorKeys().Distinct(StringComparer.Ordinal));
        OccupancyKeys(LoadLanguage("es")).Should().Equal(english);
        OccupancyKeys(LoadLanguage("pt")).Should().Equal(english);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("pt")]
    public void FormattedStrings_KeepTheirPlaceholders_InEveryLanguage(string language)
    {
        var strings = LoadLanguage(language);
        (string Key, int Placeholders)[] formatted =
        [
            ("occupancy.editor.title", 1), ("occupancy.summary.occupied", 2), ("occupancy.table.slotNumber", 1), ("occupancy.table.cableValue", 3),
            ("occupancy.action.addOn", 1), ("occupancy.action.modifyOn", 1), ("occupancy.action.removeOn", 1), ("occupancy.action.markUnidentifiedOn", 1),
            ("occupancy.action.undoOn", 1), ("occupancy.form.title", 1), ("occupancy.targets.openFor", 1),
        ];

        foreach (var (key, placeholders) in formatted)
        {
            for (var index = 0; index < placeholders; index++)
                strings[key].Should().Contain($"{{{index}}}", $"'{key}' in {language}.json is formatted with {placeholders} argument(s)");
        }
    }

    [Fact]
    public void ErrorKey_IsTheMostSpecificMessageForWhatTheStagingServiceThrew()
    {
        (Exception Thrown, string Key)[] cases =
        [
            (new OrderActionDeniedException("ord-1", RuleEvaluationResult.Deny(OrderActionType.View, "no")), "occupancy.error.denied"),
            (new OrderTargetNotFoundException("ord-1", "tgt-1"), "occupancy.error.targetNotFound"),
            (new OccupancyEntryNotFoundException("pole-1", 3), "occupancy.error.entryNotFound"),
            (new KeyNotFoundException("order"), "occupancy.error.orderNotFound"),
            (new TargetIsNotAPoleException("tgt-1", "manhole"), "occupancy.error.notAPole"),
            (new OccupancyTargetReleasedException("ord-1", "tgt-1"), "occupancy.error.targetReleased"),
            (new InvalidServiceOrderTransitionException("Completed", "Completed"), "occupancy.error.orderClosed"),
            (new InvalidOccupancyChangeException("bad"), "occupancy.error.invalid"),
            (new HttpRequestException("500"), "occupancy.error.unexpected"),
            (new InvalidOperationException("anything else"), "occupancy.error.unexpected"),
        ];

        foreach (var (thrown, key) in cases)
            OccupancyEditorMessages.ErrorKey(thrown).Should().Be(key, "for {0}", thrown.GetType().Name);

        OccupancyEditorMessages.MessageKeys.Should().Contain(cases.Select(c => c.Key));
    }

    [Fact]
    public void WarningKey_CoversEveryWarningAViewCanCarry_AndFallsBackForACodeItDoesNotKnow()
    {
        OccupancyEditorMessages.WarningKey(OccupancyProblemCodes.StaleBaseline).Should().Be("occupancy.warning.staleBaseline");
        OccupancyEditorMessages.WarningKey(OccupancyProblemCodes.EntryNotFound).Should().Be("occupancy.warning.entryNotFound");
        OccupancyEditorMessages.WarningKey(OccupancyProblemCodes.InvalidChange).Should().Be("occupancy.warning.invalidChange");
        OccupancyEditorMessages.WarningKey("occupancy.something-new").Should().Be("occupancy.warning.other");
    }

    [Theory]
    [InlineData(0, 10, 0, 1, false, false)]
    [InlineData(1, 10, 1, 1, false, false)]
    [InlineData(10, 10, 1, 1, false, false)]
    [InlineData(11, 10, 2, 1, false, true)]
    [InlineData(25, 10, 3, 2, true, true)]
    [InlineData(25, 10, 3, 3, true, false)]
    public void OrderTargetPaging_ComputesPagesAndWhichWayThePagerCanGo(int total, int pageSize, int pages, int page, bool previous, bool next)
    {
        OrderTargetPaging.TotalPages(total, pageSize).Should().Be(pages);
        OrderTargetPaging.HasPrevious(page).Should().Be(previous);
        OrderTargetPaging.HasNext(page, total, pageSize).Should().Be(next);
    }

    [Theory]
    [InlineData(0, 25, 1)]
    [InlineData(-3, 25, 1)]
    [InlineData(2, 25, 2)]
    [InlineData(9, 25, 3)]
    [InlineData(9, 0, 1)]
    public void OrderTargetPaging_Clamp_KeepsThePageInsideTheList(int page, int total, int expected)
    {
        OrderTargetPaging.Clamp(page, total, OrderTargetPaging.PageSize).Should().Be(expected);
        OrderTargetPaging.TotalPages(total, 0).Should().Be(0);
        OrderTargetPaging.PageSize.Should().BeInRange(1, 200);
    }
}
