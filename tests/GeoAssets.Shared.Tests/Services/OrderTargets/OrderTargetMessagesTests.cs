using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Shared.Components.ServiceOrders;
using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>
/// The message keys of the targets grid and the create-from-selection flow (XD01-312): every key
/// they can show exists, non-blank, in en/es/pt, the three files hold the same set, and none is unused.
/// </summary>
public sealed class OrderTargetMessagesTests
{
    [Theory]
    [InlineData(OrderTargetStatus.Pending, "orders.targets.status.Pending")]
    [InlineData(OrderTargetStatus.InProgress, "orders.targets.status.InProgress")]
    [InlineData(OrderTargetStatus.Done, "orders.targets.status.Done")]
    [InlineData(OrderTargetStatus.NotExecutable, "orders.targets.status.NotExecutable")]
    [InlineData(OrderTargetStatus.Skipped, "orders.targets.status.Skipped")]
    [InlineData("pending", "orders.targets.status.unknown")]
    [InlineData("Other", "orders.targets.status.unknown")]
    [InlineData(null, "orders.targets.status.unknown")]
    public void StatusKey_PerDefinedStatus_AndAGenericOneOtherwise(string? status, string expected) =>
        OrderTargetMessages.StatusKey(status).Should().Be(expected);

    [Theory]
    [InlineData(OrderTargetConflictLevel.None, "orders.targets.conflict.none", null)]
    [InlineData(OrderTargetConflictLevel.Warning, "orders.targets.conflict.warning", "order-target-badge order-target-badge-warning")]
    [InlineData(OrderTargetConflictLevel.Blocked, "orders.targets.conflict.blocked", "order-target-badge order-target-badge-danger")]
    [InlineData((OrderTargetConflictLevel)42, "orders.targets.conflict.none", null)]
    public void ConflictKeyAndBadge_PerLevel_NoBadgeWithoutAConflict(OrderTargetConflictLevel level, string key, string? badge)
    {
        OrderTargetMessages.ConflictKey(level).Should().Be(key);
        OrderTargetMessages.ConflictBadgeClass(level).Should().Be(badge);
    }

    [Theory]
    [InlineData(OrderTargetSelectionPlanProblem.EmptySelection, "orders.fromSelection.problem.empty")]
    [InlineData(OrderTargetSelectionPlanProblem.NoEligibleFeature, "orders.fromSelection.problem.noEligible")]
    [InlineData(OrderTargetSelectionPlanProblem.TooManyIdentifiers, "orders.fromSelection.problem.tooManyIds")]
    public void PlanProblemKey_PerProblem(OrderTargetSelectionPlanProblem problem, string expected) =>
        OrderTargetMessages.PlanProblemKey(problem).Should().Be(expected);

    /// <summary>The exception <paramref name="action"/> throws, as the framework builds it.</summary>
    private static Exception Thrown(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            return ex;
        }

        throw new InvalidOperationException("The action did not throw.");
    }

    public static TheoryData<Exception, string> Failures => new()
    {
        { new SelectionLimitExceededException(3, 1, 4), "orders.fromSelection.error.limitChanged" },
        { new SelectionTooLargeException(50_001, 50_000), "orders.fromSelection.error.tooLarge" },
        { new InvalidSelectionFenceException(SelectionFenceDefect.NotClosed), "orders.fromSelection.error.invalidFence" },
        { new OrderActionDeniedException("o1", RuleEvaluationResult.Deny(OrderActionType.Annotate, "no")), "orders.fromSelection.error.denied" },
        { new InvalidServiceOrderTransitionException("Completed", "Completed"), "orders.fromSelection.error.orderClosed" },
        { new ServiceOrderConcurrencyException("o1"), "orders.fromSelection.error.concurrent" },
        { new KeyNotFoundException("gone"), "orders.fromSelection.error.notFound" },
        { new InvalidOperationException("no policy"), "orders.fromSelection.error.noPolicy" },
        { Thrown(() => ArgumentException.ThrowIfNullOrWhiteSpace(" ")), "orders.fromSelection.error.invalid" },
        { Thrown(() => ArgumentOutOfRangeException.ThrowIfNegative(-1)), "orders.fromSelection.error.invalid" },
        { new HttpRequestException("down"), "orders.fromSelection.error.failed" },
        { new TaskCanceledException("timeout"), "orders.fromSelection.error.failed" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void SelectionErrorKey_PerTranslatedProblem_AndAGenericOneOtherwise(Exception failure, string expected) =>
        OrderTargetMessages.SelectionErrorKey(failure).Should().Be(expected);

    [Fact]
    public void SelectionErrorKey_Null_Throws() =>
        FluentActions.Invoking(() => OrderTargetMessages.SelectionErrorKey(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void MessageKeys_HoldEveryKeyTheMappingsReturn() =>
        OrderTargetMessages.MessageKeys.Should().Contain(Failures.Select(row => (string)row[1]));

    // ── i18n ─────────────────────────────────────────────────────────────────

    private static readonly string[] _prefixes = ["orders.targets.", "orders.fromSelection.", "orders.pager."];

    /// <summary>The keys the grid, pager, preview panel and create form name literally.</summary>
    private static readonly string[] _staticKeys =
    [
        "orders.targets.caption", "orders.targets.filterStatus", "orders.targets.anyStatus", "orders.targets.filterConflict",
        "orders.targets.anyConflict", "orders.targets.pageSize", "orders.targets.retry", "orders.targets.emptyFiltered",
        "orders.fromSelection.previewTitle", "orders.fromSelection.planFence", "orders.fromSelection.planIds",
        "orders.fromSelection.previewAgain", "orders.fromSelection.previewing", "orders.fromSelection.selected",
        "orders.fromSelection.matched", "orders.fromSelection.alreadyInOrder", "orders.fromSelection.toAdd",
        "orders.fromSelection.truncated", "orders.fromSelection.warnings", "orders.fromSelection.blocked",
        "orders.fromSelection.notResolved", "orders.fromSelection.nothingToAdd", "orders.fromSelection.conflictNotice",
        "orders.fromSelection.truncationNotice", "orders.fromSelection.acceptTruncation", "orders.fromSelection.previewCaption",
        "orders.fromSelection.previewPager", "orders.fromSelection.apply", "orders.fromSelection.applying", "orders.fromSelection.skip",
        "orders.fromSelection.use", "orders.fromSelection.noType", "orders.fromSelection.typeOption", "orders.fromSelection.otherTypes",
        "orders.fromSelection.limitWarning", "orders.fromSelection.save",
        // reused from the occupancy and assets-table strings
        "occupancy.targets.target", "occupancy.targets.pager", "occupancy.targets.empty", "occupancy.targets.loadError",
        "occupancy.targets.caption", "occupancy.table.status", "occupancy.table.actions", "occupancy.editor.loading",
        "assetsTable.pagerSummary",
    ];

    private static IEnumerable<string> UsedKeys() =>
        _staticKeys
            .Concat(OrderTargetMessages.MessageKeys)
            .Concat(OrderTargetsTable.Columns.Select(c => c.LabelKey))
            .Concat(ServiceOrderPager.Steps.Select(ServiceOrderPager.StepLabelKey));

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
    public void EveryKeyUsed_ExistsWithANonBlankValue(string language)
    {
        var strings = LoadLanguage(language);

        foreach (var key in UsedKeys())
            strings.Should().ContainKey(key).WhoseValue.Should().NotBeNullOrWhiteSpace($"'{key}' must be translated in {language}.json");
    }

    [Fact]
    public void NewKeys_AreTheSameSetInAllThreeLanguages_AndNoneIsUnused()
    {
        static string[] OwnKeys(Dictionary<string, string> strings) =>
            [.. strings.Keys.Where(k => _prefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal))).Order(StringComparer.Ordinal)];

        var english = OwnKeys(LoadLanguage("en"));

        english.Should().BeEquivalentTo(UsedKeys().Where(k => _prefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal))).Distinct(StringComparer.Ordinal));
        OwnKeys(LoadLanguage("es")).Should().Equal(english);
        OwnKeys(LoadLanguage("pt")).Should().Equal(english);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("pt")]
    public void FormattedMessages_KeepTheirPlaceholders(string language)
    {
        var strings = LoadLanguage(language);

        strings["orders.fromSelection.planIds"].Should().Contain("{0}");
        strings["orders.fromSelection.conflictNotice"].Should().Contain("{0}").And.Contain("{1}");
        strings["orders.fromSelection.truncationNotice"].Should().Contain("{0}").And.Contain("{1}");
        strings["orders.fromSelection.acceptTruncation"].Should().Contain("{0}");
        strings["orders.fromSelection.apply"].Should().Contain("{0}");
        strings["orders.fromSelection.use"].Should().Contain("{0}");
        strings["orders.fromSelection.typeOption"].Should().Contain("{0}").And.Contain("{1}").And.Contain("{2}");
        strings["orders.fromSelection.otherTypes"].Should().Contain("{0}");
        strings["orders.fromSelection.limitWarning"].Should().Contain("{0}");
    }
}
