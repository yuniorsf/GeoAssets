using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using GeoAssets.Shared.Services.SharedUse;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.SharedUse;

/// <summary>
/// The localization keys of the shared-use dispatch dialog and the received-order badge
/// (XD01-316): which key each reason, skip reason and failure maps to (T-UI-18), and that every
/// key the UI can show exists, non-blank, in the three language files, which hold the same set.
/// </summary>
public sealed partial class SharedUseDispatchMessagesTests
{
    /// <summary>The keys the components name literally in their markup and code-behind.</summary>
    private static readonly string[] _staticKeys =
    [
        "orders.receivedBadge", "orders.receivedBadgeTitle",
        "sharedUse.targets.activities", "sharedUse.targets.readOnlyCaption", "sharedUse.targets.workCaption",
        "sharedUse.dispatch.title", "sharedUse.dispatch.intro", "sharedUse.dispatch.loading", "sharedUse.dispatch.nothingNew",
        "sharedUse.dispatch.confirm", "sharedUse.dispatch.dispatching", "sharedUse.dispatch.result", "sharedUse.dispatch.close",
        "sharedUse.summary.poles", "sharedUse.summary.recipients", "sharedUse.summary.newOrders",
        "sharedUse.recipients.caption", "sharedUse.recipients.organization", "sharedUse.recipients.poles", "sharedUse.recipients.state",
        "sharedUse.recipients.new", "sharedUse.recipients.dispatched", "sharedUse.recipients.pager",
        "sharedUse.skipped.show", "sharedUse.skipped.hide", "sharedUse.skipped.caption", "sharedUse.skipped.pole", "sharedUse.skipped.slot",
        "sharedUse.skipped.status", "sharedUse.skipped.reason", "sharedUse.skipped.beyondSlots", "sharedUse.skipped.pager", "sharedUse.skipped.loadError",
    ];

    private static IEnumerable<string> UiKeys() =>
        _staticKeys.Concat(SharedUseDispatchMessages.MessageKeys).Concat(SharedUseDispatchDialogState.CaveatKeys);

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the test runs from inside the repository");
        return directory!;
    }

    private static Dictionary<string, string> LoadLanguage(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(RepositoryRoot().FullName, "apps", "GeoAssets.Shared", "wwwroot", "i18n", $"{language}.json")))!;

    // ── Which key (T-UI-18) ───────────────────────────────────────────────────

    [Fact]
    public void BlockKey_EveryReason_HasItsOwnKey_AndAnUnknownOneAGenericKey()
    {
        var keys = Enum.GetValues<SharedUseDispatchBlockReason>().Select(SharedUseDispatchMessages.BlockKey).ToList();

        keys.Should().OnlyHaveUniqueItems().And.NotContain("sharedUse.block.other").And.BeSubsetOf(SharedUseDispatchMessages.MessageKeys);
        SharedUseDispatchMessages.BlockKey((SharedUseDispatchBlockReason)99).Should().Be("sharedUse.block.other");
    }

    [Fact]
    public void SkipReasonKey_EveryReason_HasItsOwnKey_AndAnUnknownOneAGenericKey()
    {
        var keys = Enum.GetValues<SharedUseSkipReason>().Select(SharedUseDispatchMessages.SkipReasonKey).ToList();

        keys.Should().OnlyHaveUniqueItems().And.NotContain("sharedUse.skipReason.other").And.BeSubsetOf(SharedUseDispatchMessages.MessageKeys);
        SharedUseDispatchMessages.SkipReasonKey((SharedUseSkipReason)99).Should().Be("sharedUse.skipReason.other");
    }

    public static TheoryData<Exception, string> Failures => new()
    {
        { new OrderActionDeniedException("ord-1", RuleEvaluationResult.Deny(OrderActionType.Dispatch, "owner only")), "sharedUse.error.denied" },
        { new ServiceOrderConcurrencyException("ord-1"), "sharedUse.error.changed" },
        { new InvalidServiceOrderTransitionException("Completed", "Completed"), "sharedUse.error.invalidState" },
        { new SharedUseDispatchBlockedException("ord-1", SharedUseDispatchBlockReason.NoTargets), "sharedUse.block.noTargets" },
        { new SharedUseDispatchBlockedException("ord-1", SharedUseDispatchBlockReason.TargetsWithoutActivity, ["t-1"]), "sharedUse.block.targetsWithoutActivity" },
        { new OrderTypeHasNoFanOutException("plain", "no fan-out"), "sharedUse.error.notShared" },
        { new OrderTypeNotRegisteredException("ghost", "no such type"), "sharedUse.error.notFound" },
        { new KeyNotFoundException("no such order"), "sharedUse.error.notFound" },
        { new HttpRequestException("503"), "sharedUse.error.unexpected" },
        { new InvalidOperationException("something else"), "sharedUse.error.unexpected" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void ErrorKey_MapsEachFailureOfTheDispatchService_MostSpecificFirst(Exception failure, string expectedKey)
    {
        ArgumentNullException.ThrowIfNull(failure);

        var key = SharedUseDispatchMessages.ErrorKey(failure);

        key.Should().Be(expectedKey);
        SharedUseDispatchMessages.MessageKeys.Should().Contain(key);
    }

    // ── The language files ────────────────────────────────────────────────────

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("pt")]
    public void EveryKeyTheUiUses_ExistsWithANonBlankValue(string language)
    {
        var strings = LoadLanguage(language);

        foreach (var key in UiKeys())
            strings.Should().ContainKey(key).WhoseValue.Should().NotBeNullOrWhiteSpace($"'{key}' must be translated in {language}.json");
    }

    [Fact]
    public void SharedUseKeys_AreTheSameSetInAllThreeLanguages_AndNoneIsUnused()
    {
        static string[] SharedUseKeys(Dictionary<string, string> strings) =>
            [.. strings.Keys.Where(k => k.StartsWith("sharedUse.", StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

        var english = SharedUseKeys(LoadLanguage("en"));

        SharedUseKeys(LoadLanguage("es")).Should().Equal(english);
        SharedUseKeys(LoadLanguage("pt")).Should().Equal(english);
        english.Should().BeEquivalentTo(
            UiKeys().Where(k => k.StartsWith("sharedUse.", StringComparison.Ordinal)).Distinct(StringComparer.Ordinal),
            "a key nothing shows is a translation nobody maintains");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("pt")]
    public void EveryTranslation_KeepsThePlaceholdersOfTheEnglishText(string language)
    {
        var english = LoadLanguage("en");
        var strings = LoadLanguage(language);

        foreach (var key in UiKeys())
        {
            Placeholders(strings[key]).Should().Equal(Placeholders(english[key]), $"'{key}' in {language}.json takes the same arguments");
        }
    }

    [Fact]
    public void TheKeysTheComponentsNameLiterally_AreExactlyTheStaticKeysOfThisTest()
    {
        var components = Path.Combine(RepositoryRoot().FullName, "apps", "GeoAssets.Shared", "Components", "ServiceOrders");
        string[] files =
        [
            "SharedUseDispatchDialog.razor", "SharedUseDispatchDialog.razor.cs", "OrderTargetOccupancyPanel.razor", "OrderTargetsTable.razor", "ServiceOrderList.razor",
        ];

        var named = files
            .SelectMany(file => LiteralKey().Matches(File.ReadAllText(Path.Combine(components, file))).Select(match => match.Groups["key"].Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        named.Should().Equal(_staticKeys.Order(StringComparer.Ordinal), "the list above is what proves every literal key is translated");
    }

    private static List<string> Placeholders(string text) =>
        [.. Placeholder().Matches(text).Select(match => match.Value).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"\{\d+\}", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Placeholder();

    /// <summary>A quoted key of this slice's two families, as it appears in markup or code.</summary>
    [GeneratedRegex("\"(?<key>(sharedUse\\.|orders\\.received)[A-Za-z.]+)\"", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LiteralKey();
}
