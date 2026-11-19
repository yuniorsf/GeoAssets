using System.Globalization;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Shared.Components.Layers;
using Xunit;

namespace GeoAssets.Shared.Tests.Components.Layers;

/// <summary>
/// The pure decisions behind the asset-type editor's role tags (XD01-367; this repo has no
/// bUnit), and that every localization key they can ask for exists in all three languages.
/// </summary>
public class AssetTypeRolesInputTests
{
    private static string[] Distinct(int count) => [.. Enumerable.Range(0, count).Select(i => $"role-{i}")];

    // ── Add ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Add_SingleRole_AppendsIt()
    {
        var result = AssetTypeRolesInput.Add(["a"], "isp:pole");

        result.IsValid.Should().BeTrue();
        result.Error.Should().BeNull();
        result.OffendingRole.Should().BeNull();
        result.Roles.Should().Equal("a", "isp:pole");
    }

    [Theory]
    [InlineData("b,c")]
    [InlineData("b c")]
    [InlineData("b, c")]
    [InlineData("  b ,\tc\r\n")]
    [InlineData(",b,,c,")]
    public void Add_SeveralRolesSeparatedByCommasOrWhitespace_AppendsEachTrimmed(string text)
    {
        var result = AssetTypeRolesInput.Add(["a"], text);

        result.IsValid.Should().BeTrue();
        result.Roles.Should().Equal("a", "b", "c");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(" , ,\t")]
    public void Add_BlankText_ReturnsTheSameListAndIsValid(string? text)
    {
        IReadOnlyList<string> current = ["a"];

        var result = AssetTypeRolesInput.Add(current, text);

        result.IsValid.Should().BeTrue();
        result.Roles.Should().BeSameAs(current);
    }

    [Fact]
    public void Add_MalformedRole_ReturnsTheUnchangedListWithTheErrorAndTheRole()
    {
        IReadOnlyList<string> current = ["a"];

        var result = AssetTypeRolesInput.Add(current, "good Bad_Role");

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(AssetTypeRoleDefect.MalformedRole);
        result.OffendingRole.Should().Be("Bad_Role");
        result.Roles.Should().BeSameAs(current);
    }

    [Fact]
    public void Add_RoleAlreadyOnTheType_IsADuplicateAndChangesNothing()
    {
        IReadOnlyList<string> current = ["a", "b"];

        var result = AssetTypeRolesInput.Add(current, "b");

        result.Error.Should().Be(AssetTypeRoleDefect.DuplicateRole);
        result.OffendingRole.Should().Be("b");
        result.Roles.Should().BeSameAs(current);
    }

    [Fact]
    public void Add_RepeatedInsideTheTypedText_IsADuplicate()
    {
        var result = AssetTypeRolesInput.Add([], "x, x");

        result.Error.Should().Be(AssetTypeRoleDefect.DuplicateRole);
        result.OffendingRole.Should().Be("x");
        result.Roles.Should().BeEmpty();
    }

    [Fact]
    public void Add_RoleTooLong_ReturnsRoleTooLongWithTheRole()
    {
        var role = new string('a', AssetTypeRoles.MaxLength + 1);

        var result = AssetTypeRolesInput.Add(["a"], role);

        result.Error.Should().Be(AssetTypeRoleDefect.RoleTooLong);
        result.OffendingRole.Should().Be(role);
        result.Roles.Should().Equal("a");
    }

    [Fact]
    public void Add_TheSeventeenthRole_IsTooManyAndChangesNothing()
    {
        IReadOnlyList<string> current = Distinct(AssetTypeRoles.MaxCount);

        var result = AssetTypeRolesInput.Add(current, "one-more");

        result.Error.Should().Be(AssetTypeRoleDefect.TooManyRoles);
        result.OffendingRole.Should().BeNull();
        result.Roles.Should().BeSameAs(current);
    }

    [Fact]
    public void Add_TheSixteenthRole_IsAccepted()
    {
        var result = AssetTypeRolesInput.Add(Distinct(AssetTypeRoles.MaxCount - 1), "last-one");

        result.IsValid.Should().BeTrue();
        result.Roles.Should().HaveCount(AssetTypeRoles.MaxCount);
    }

    [Fact]
    public void Add_NullCurrent_Throws()
    {
        var act = () => AssetTypeRolesInput.Add(null!, "a");

        act.Should().Throw<ArgumentNullException>();
    }

    // ── Remove ───────────────────────────────────────────────────────────────

    [Fact]
    public void Remove_DropsOnlyTheNamedRole()
    {
        AssetTypeRolesInput.Remove(["a", "b", "c"], "b").Should().Equal("a", "c");
    }

    [Fact]
    public void Remove_IsOrdinalAndCaseSensitive_AndAnUnknownRoleChangesNothing()
    {
        AssetTypeRolesInput.Remove(["isp:pole"], "ISP:POLE").Should().Equal("isp:pole");
        AssetTypeRolesInput.Remove(["a"], "zzz").Should().Equal("a");
    }

    [Fact]
    public void Remove_NullCurrent_Throws()
    {
        var act = () => AssetTypeRolesInput.Remove(null!, "a");

        act.Should().Throw<ArgumentNullException>();
    }

    // ── Error key / arguments ────────────────────────────────────────────────

    [Theory]
    [InlineData(AssetTypeRoleDefect.TooManyRoles, "map.layers.roles.error.tooMany")]
    [InlineData(AssetTypeRoleDefect.RoleTooLong, "map.layers.roles.error.tooLong")]
    [InlineData(AssetTypeRoleDefect.DuplicateRole, "map.layers.roles.error.duplicate")]
    [InlineData(AssetTypeRoleDefect.MalformedRole, "map.layers.roles.error.malformed")]
    public void ErrorKey_MapsEveryDefect(AssetTypeRoleDefect defect, string expected) =>
        AssetTypeRolesInput.ErrorKey(defect).Should().Be(expected);

    [Fact]
    public void ErrorKey_CoversEveryDefectWithADistinctKey() =>
        Enum.GetValues<AssetTypeRoleDefect>().Select(AssetTypeRolesInput.ErrorKey).Should().OnlyHaveUniqueItems();

    [Fact]
    public void ErrorArguments_TooManyRoles_IsTheMaxCount() =>
        AssetTypeRolesInput.ErrorArguments(AssetTypeRoleDefect.TooManyRoles, null).Should().Equal(AssetTypeRoles.MaxCount);

    [Fact]
    public void ErrorArguments_RoleTooLong_IsTheRoleAndTheMaxLength() =>
        AssetTypeRolesInput.ErrorArguments(AssetTypeRoleDefect.RoleTooLong, "long").Should().Equal("long", AssetTypeRoles.MaxLength);

    [Theory]
    [InlineData(AssetTypeRoleDefect.MalformedRole)]
    [InlineData(AssetTypeRoleDefect.DuplicateRole)]
    public void ErrorArguments_MalformedAndDuplicate_AreTheRole(AssetTypeRoleDefect defect) =>
        AssetTypeRolesInput.ErrorArguments(defect, "bad").Should().Equal("bad");

    [Theory]
    [InlineData(AssetTypeRoleDefect.RoleTooLong)]
    [InlineData(AssetTypeRoleDefect.MalformedRole)]
    [InlineData(AssetTypeRoleDefect.DuplicateRole)]
    public void ErrorArguments_NullRole_BecomesAnEmptyString(AssetTypeRoleDefect defect) =>
        AssetTypeRolesInput.ErrorArguments(defect, null)[0].Should().Be(string.Empty);

    // ── Localization ─────────────────────────────────────────────────────────

    private static readonly string[] _labelKeys =
    [
        "map.layers.roles", "map.layers.roles.none", "map.layers.roles.placeholder",
        "map.layers.roles.add", "map.layers.roles.remove", "map.layers.roles.help"
    ];

    private static IEnumerable<string> EditorKeys() =>
        _labelKeys.Concat(Enum.GetValues<AssetTypeRoleDefect>().Select(AssetTypeRolesInput.ErrorKey));

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
    public void RoleKeys_ExistInEveryLanguage(string language)
    {
        var strings = LoadLanguage(language);

        foreach (var key in EditorKeys())
            strings.Should().ContainKey(key).WhoseValue.Should().NotBeNullOrWhiteSpace($"'{key}' in {language}.json");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("pt")]
    public void ErrorMessages_FormatWithTheArgumentsTheEditorPasses(string language)
    {
        var strings = LoadLanguage(language);

        foreach (var defect in Enum.GetValues<AssetTypeRoleDefect>())
        {
            var template = strings[AssetTypeRolesInput.ErrorKey(defect)];
            var args = AssetTypeRolesInput.ErrorArguments(defect, "some-role");

            var message = string.Format(CultureInfo.InvariantCulture, template, args);

            message.Should().NotContain("{", $"{defect} in {language}.json must only use the supplied arguments");
        }
    }
}
