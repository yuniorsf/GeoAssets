using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using GeoAssets.Shared.Services.OrderTargets;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.OrderTargets;

/// <summary>
/// The message keys of the per-target work editor and the map legend (XD01-313): every key exists,
/// non-blank, in en/es/pt, the three files hold the same <c>targetWork.*</c> set, none is unused,
/// and the keys the components name literally are exactly the ones listed here.
/// </summary>
public sealed partial class OrderTargetWorkMessagesTests
{
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
        { new ServiceOrderConcurrencyException("o1"), "targetWork.error.concurrent" },
        { new OrderActionDeniedException("o1", RuleEvaluationResult.Deny(OrderActionType.Annotate, "no")), "targetWork.error.denied" },
        { new KeyNotFoundException("gone"), "targetWork.error.notFound" },
        { Thrown(() => ArgumentException.ThrowIfNullOrWhiteSpace(" ")), "targetWork.error.invalid" },
        { Thrown(() => ArgumentOutOfRangeException.ThrowIfNegative(-1)), "targetWork.error.invalid" },
        { new InvalidOperationException("bad move"), "targetWork.error.invalid" },
        { new HttpRequestException("down"), "targetWork.error.failed" },
        { new TaskCanceledException("timeout"), "targetWork.error.failed" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void ErrorKey_PerProblem_AndAGenericOneOtherwise(Exception failure, string expected) =>
        OrderTargetWorkMessages.ErrorKey(failure).Should().Be(expected);

    [Fact]
    public void ErrorKey_Null_Throws() =>
        FluentActions.Invoking(() => OrderTargetWorkMessages.ErrorKey(null!)).Should().Throw<ArgumentNullException>();

    [Fact]
    public void ErrorKeys_HoldEveryKeyTheMappingReturns_AndTheTwoAfterAConflict() =>
        OrderTargetWorkMessages.ErrorKeys.Should().BeEquivalentTo(
            Failures.Select(row => (string)row[1]).Append(OrderTargetWorkMessages.ConcurrentReloadFailedKey).Distinct(StringComparer.Ordinal));

    // ── i18n ─────────────────────────────────────────────────────────────────

    private const string Prefix = "targetWork.";

    /// <summary>The keys the work editor, the panel and the grid name literally.</summary>
    private static readonly string[] _staticKeys =
    [
        "targetWork.open", "targetWork.openFor", "targetWork.title", "targetWork.close", "targetWork.droppedActivities",
        "targetWork.activities", "targetWork.noActivities", "targetWork.status", "targetWork.note", "targetWork.noteCount",
        "targetWork.save", "targetWork.saving", "targetWork.cancel", "targetWork.saved", "targetWork.map.legend",
    ];

    private static IEnumerable<string> UsedKeys() =>
        _staticKeys
            .Concat(OrderTargetWorkMessages.ErrorKeys)
            .Concat(OrderTargetMapState.Legend.Select(state => OrderTargetMapState.LegendKey(state)!));

    private static DirectoryInfo RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the test runs from inside the repository");
        return directory!;
    }

    private static Dictionary<string, string> LoadLanguage(string language)
    {
        var path = Path.Combine(RepositoryRoot().FullName, "apps", "GeoAssets.Shared", "wwwroot", "i18n", $"{language}.json");
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
    public void TheKeys_AreTheSameSetInAllThreeLanguages_AndNoneIsUnused()
    {
        static string[] OwnKeys(Dictionary<string, string> strings) =>
            [.. strings.Keys.Where(k => k.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal)];

        var english = OwnKeys(LoadLanguage("en"));

        english.Should().BeEquivalentTo(UsedKeys().Distinct(StringComparer.Ordinal));
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

        strings["targetWork.openFor"].Should().Contain("{0}");
        strings["targetWork.title"].Should().Contain("{0}");
        strings["targetWork.droppedActivities"].Should().Contain("{0}");
        strings["targetWork.noteCount"].Should().Contain("{0}").And.Contain("{1}");
        strings["targetWork.saved"].Should().Contain("{0}");
    }

    [Fact]
    public void TheKeysTheComponentsNameLiterally_AreExactlyTheStaticKeysOfThisTest()
    {
        var root = RepositoryRoot().FullName;
        var shared = Path.Combine(root, "apps", "GeoAssets.Shared", "Components", "ServiceOrders");
        var neutralOperator = Path.Combine(root, "verticals", "NeutralOperator", "GeoAssets.NeutralOperator.UI", "Components", "ServiceOrders");
        string[] files =
        [
            Path.Combine(shared, "OrderTargetWorkEditor.razor"), Path.Combine(shared, "OrderTargetWorkEditor.razor.cs"),
            Path.Combine(neutralOperator, "OrderTargetOccupancyPanel.razor"), Path.Combine(shared, "OrderTargetsTable.razor"),
        ];

        var named = files
            .SelectMany(file => LiteralKey().Matches(File.ReadAllText(file)).Select(match => match.Groups["key"].Value))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        named.Should().Equal(_staticKeys.Order(StringComparer.Ordinal));
    }

    /// <summary>A quoted <c>targetWork.*</c> key, as it appears in markup or code.</summary>
    [GeneratedRegex("\"(?<key>targetWork\\.[A-Za-z.]+)\"", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LiteralKey();
}
