using FluentAssertions;
using Xunit;

namespace GeoAssets.Tools.SharedUseImport.Tests;

public sealed class SharedUseImportCommandLineTests
{
    private const string Organization = "0000000a-0000-0000-0000-000000000001";
    private const string Connection = "Host=localhost;Database=geoassets";

    private static readonly string[] Complete =
        ["--shared-uses", "su.csv", "--mapping", "map.json", "--organization", Organization, "--report", "out.json"];

    [Fact]
    public void Parse_EveryOption_InAnyOrder()
    {
        var (options, error) = SharedUseImportCommandLine.Parse(
            ["--report", "out.json", "--dry-run", "--organization", Organization, "--mapping", "map.json", "--shared-uses", "su.csv"], Connection);

        error.Should().BeNull();
        options.Should().Be(new SharedUseImportOptions("su.csv", "map.json", "out.json", Guid.Parse(Organization), DryRun: true, Connection));
    }

    [Fact]
    public void Parse_WithoutDryRun_IsARealImport()
    {
        var (options, _) = SharedUseImportCommandLine.Parse(Complete, Connection);

        options!.DryRun.Should().BeFalse();
    }

    [Theory]
    [InlineData("--verbose", "Unknown argument '--verbose'.")]
    [InlineData("extra", "Unknown argument 'extra'.")]
    public void Parse_UnknownArgument_IsAnError(string argument, string expected)
    {
        var (options, error) = SharedUseImportCommandLine.Parse([.. Complete, argument], Connection);

        options.Should().BeNull();
        error.Should().Be(expected);
    }

    public static TheoryData<string[]> OptionsWithoutValue => new()
    {
        new[] { "--mapping" },
        new[] { "--mapping", "--dry-run" },
        new[] { "--mapping", " " },
    };

    [Theory]
    [MemberData(nameof(OptionsWithoutValue))]
    public void Parse_OptionWithoutValue_IsAnError(string[] args)
    {
        var (_, error) = SharedUseImportCommandLine.Parse(args, Connection);

        error.Should().Be("--mapping needs a value.");
    }

    [Fact]
    public void Parse_OptionGivenTwice_IsAnError()
    {
        var (_, error) = SharedUseImportCommandLine.Parse([.. Complete, "--report", "other.json"], Connection);

        error.Should().Be("--report is given more than once.");
    }

    [Fact]
    public void Parse_DryRunGivenTwice_IsAnError()
    {
        var (_, error) = SharedUseImportCommandLine.Parse([.. Complete, "--dry-run", "--dry-run"], Connection);

        error.Should().Be("--dry-run is given more than once.");
    }

    [Theory]
    [InlineData("--shared-uses")]
    [InlineData("--mapping")]
    [InlineData("--organization")]
    [InlineData("--report")]
    public void Parse_MissingRequiredOption_IsAnError(string missing)
    {
        var index = Array.IndexOf(Complete, missing);
        string[] args = [.. Complete.Take(index), .. Complete.Skip(index + 2)];

        var (_, error) = SharedUseImportCommandLine.Parse(args, Connection);

        error.Should().Be($"{missing} is required.");
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Parse_OrganizationThatIsNotAnId_IsAnError(string organization)
    {
        var (_, error) = SharedUseImportCommandLine.Parse(
            ["--shared-uses", "su.csv", "--mapping", "map.json", "--organization", organization, "--report", "out.json"], Connection);

        error.Should().Be("--organization must be a non-empty organization id (GUID).");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Parse_NoConnectionString_IsAnError(string? connection)
    {
        var (_, error) = SharedUseImportCommandLine.Parse(Complete, connection);

        error.Should().Be($"Set {SharedUseImportOptions.ConnectionStringVariable} to the PostgreSQL connection string.");
    }

    [Fact]
    public void Parse_NullArguments_Throws()
    {
        var act = () => SharedUseImportCommandLine.Parse(null!, Connection);

        act.Should().Throw<ArgumentNullException>();
    }
}
