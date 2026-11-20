using FluentAssertions;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests;

public sealed class DataMoveCommandLineTests
{
    private const string Source = "Host=kernel-db;Port=5432;Database=geoassets;Username=svc;Password=kernel-secret";
    private const string Target = "Host=no-db;Port=5432;Database=geoassets_neutral_operator;Username=svc;Password=target-secret";

    private static DataMoveCommandLineResult Parse(string[] args, string? sourceVariable = null, string? targetVariable = null) =>
        DataMoveCommandLine.Parse(args, sourceVariable, targetVariable);

    [Theory]
    [InlineData("plan", "Plan")]
    [InlineData("copy", "Copy")]
    [InlineData("verify", "Verify")]
    public void Parse_WithAVerb_SelectsIt(string verb, string expected)
    {
        var result = Parse([verb, "--source", Source, "--target", Target]);

        result.Error.Should().BeNull();
        result.Options!.Verb.ToString().Should().Be(expected);
        result.Options.SourceConnectionString.Should().Be(Source);
        result.Options.TargetConnectionString.Should().Be(Target);
    }

    [Fact]
    public void Parse_WithoutAVerb_IsADryRunPlan_WithDefaults()
    {
        var result = Parse(["--source", Source, "--target", Target]);

        result.Options!.Verb.Should().Be(DataMoveVerb.Plan);
        result.Options.BatchSize.Should().Be(DataMoveOptions.DefaultBatchSize).And.Be(500);
        result.Options.IncludePendingOutbox.Should().BeFalse();
        result.HelpRequested.Should().BeFalse();
    }

    [Fact]
    public void Parse_Remove_WithConfirm_Succeeds()
    {
        var result = Parse(["remove", "--confirm", "--source", Source, "--target", Target]);

        result.Options!.Verb.Should().Be(DataMoveVerb.Remove);
    }

    [Fact]
    public void Parse_Remove_WithoutConfirm_IsAUsageError()
    {
        var result = Parse(["remove", "--source", Source, "--target", Target]);

        result.Options.Should().BeNull();
        result.Error.Should().Contain("--confirm");
    }

    [Theory]
    [InlineData("plan")]
    [InlineData("copy")]
    [InlineData("verify")]
    public void Parse_ConfirmOnAnythingButRemove_IsAUsageError(string verb)
    {
        var result = Parse([verb, "--confirm", "--source", Source, "--target", Target]);

        result.Options.Should().BeNull();
        result.Error.Should().Contain("remove only");
    }

    [Theory]
    [InlineData("plan", true)]
    [InlineData("copy", true)]
    [InlineData("verify", false)]
    public void Parse_IncludePendingOutbox_BelongsToPlanAndCopyOnly(string verb, bool accepted)
    {
        var result = Parse([verb, "--include-pending-outbox", "--source", Source, "--target", Target]);

        (result.Options is not null).Should().Be(accepted);
        if (accepted) result.Options!.IncludePendingOutbox.Should().BeTrue();
        else result.Error.Should().Contain("plan and copy only");
    }

    [Fact]
    public void Parse_IncludePendingOutbox_WithRemove_IsAUsageError()
    {
        var result = Parse(["remove", "--confirm", "--include-pending-outbox", "--source", Source, "--target", Target]);

        result.Options.Should().BeNull();
        result.Error.Should().Contain("plan and copy only");
    }

    [Fact]
    public void Parse_WithoutOptions_FallsBackToTheEnvironmentVariables()
    {
        var result = Parse(["copy"], Source, Target);

        result.Options!.SourceConnectionString.Should().Be(Source);
        result.Options.TargetConnectionString.Should().Be(Target);
    }

    [Fact]
    public void Parse_AnOptionWinsOverItsEnvironmentVariable()
    {
        const string other = "Host=other-db;Port=5432;Database=other";

        var result = Parse(["--source", other], Source, Target);

        result.Options!.SourceConnectionString.Should().Be(other);
        result.Options.TargetConnectionString.Should().Be(Target);
    }

    [Fact]
    public void Parse_WithoutASource_NamesTheOptionAndTheVariable()
    {
        var result = Parse(["--target", Target]);

        result.Options.Should().BeNull();
        result.Error.Should().Contain("--source").And.Contain(DataMoveOptions.SourceVariable).And.Contain("GEOASSETS_KERNEL_DB");
    }

    [Fact]
    public void Parse_WithoutATarget_NamesTheOptionAndTheVariable()
    {
        var result = Parse(["--source", Source], null, " ");

        result.Options.Should().BeNull();
        result.Error.Should().Contain("--target").And.Contain(DataMoveOptions.TargetVariable).And.Contain("GEOASSETS_NEUTRAL_OPERATOR_DB");
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    public void Parse_Help_IsRequested_EvenAmongOtherArguments(string flag)
    {
        var result = Parse(["copy", flag, "--bogus"]);

        result.HelpRequested.Should().BeTrue();
        result.Options.Should().BeNull();
        result.Error.Should().BeNull();
    }

    [Theory]
    [InlineData("--bogus")]
    [InlineData("-x")]
    [InlineData("--sourcee")]
    public void Parse_AnUnknownOption_IsAUsageErrorNamingIt(string option)
    {
        var result = Parse([option, "--source", Source, "--target", Target]);

        result.Options.Should().BeNull();
        result.Error.Should().Contain($"Unknown option '{option}'");
    }

    [Fact]
    public void Parse_AnUnknownVerb_IsAUsageError()
    {
        var result = Parse(["migrate", "--source", Source, "--target", Target]);

        result.Error.Should().Contain("Unknown verb 'migrate'");
    }

    [Fact]
    public void Parse_TwoVerbs_IsAUsageError()
    {
        var result = Parse(["copy", "verify", "--source", Source, "--target", Target]);

        result.Error.Should().Contain("give one verb");
    }

    [Theory]
    [InlineData("--source")]
    [InlineData("--target")]
    [InlineData("--batch-size")]
    public void Parse_AnOptionWithoutAValue_IsAUsageError(string option)
    {
        var atTheEnd = Parse([option]);
        var beforeAnOption = Parse([option, "--confirm"]);
        var blank = Parse([option, " "]);

        foreach (var result in new[] { atTheEnd, beforeAnOption, blank })
        {
            result.Options.Should().BeNull();
            result.Error.Should().Be($"{option} needs a value.");
        }
    }

    [Theory]
    [InlineData("--source")]
    [InlineData("--target")]
    [InlineData("--batch-size")]
    public void Parse_AValuedOptionGivenTwice_IsAUsageError(string option)
    {
        var result = Parse([option, "1", option, "2"]);

        result.Error.Should().Be($"{option} is given more than once.");
    }

    [Theory]
    [InlineData("--include-pending-outbox")]
    [InlineData("--confirm")]
    public void Parse_AFlagGivenTwice_IsAUsageError(string flag)
    {
        var result = Parse([flag, flag]);

        result.Error.Should().Be($"{flag} is given more than once.");
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("250", 250)]
    [InlineData("10000", 10_000)]
    public void Parse_ABatchSizeInRange_IsUsed(string value, int expected)
    {
        var result = Parse(["--batch-size", value, "--source", Source, "--target", Target]);

        result.Options!.BatchSize.Should().Be(expected);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("10001")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("12.5")]
    [InlineData("many")]
    [InlineData("99999999999999999999")]
    public void Parse_ABatchSizeOutOfRangeOrNotANumber_IsAUsageError(string value)
    {
        var result = Parse(["--batch-size", value, "--source", Source, "--target", Target]);

        result.Options.Should().BeNull();
        result.Error.Should().Contain("--batch-size must be a whole number from 1 to 10000");
    }

    [Fact]
    public void Parse_AnUnparsableSourceConnectionString_IsAUsageError_WithoutEchoingIt()
    {
        var result = Parse(["--source", "not a connection string; password=leaky", "--target", Target]);

        result.Options.Should().BeNull();
        result.Error.Should().Contain("kernel's connection string is not a valid PostgreSQL connection string");
        result.Error.Should().NotContain("leaky");
    }

    [Fact]
    public void Parse_AnUnparsableTargetConnectionString_IsAUsageError_WithoutEchoingIt()
    {
        var result = Parse(["--source", Source, "--target", "Host=x;Bogus=1;Password=leaky"]);

        result.Options.Should().BeNull();
        result.Error.Should().Contain("NeutralOperator.Server's connection string is not a valid PostgreSQL connection string");
        result.Error.Should().NotContain("leaky");
    }

    [Theory]
    [InlineData("Host=db;Port=5432;Database=geoassets;Username=a;Password=p1", "Host=db;Port=5432;Database=geoassets;Username=b;Password=p2")]
    [InlineData("Host=DB;Database=geoassets", "Host=db;Port=5432;Database=GEOASSETS")]
    public void Parse_TheSameDatabaseForSourceAndTarget_IsAUsageError_EvenWithOtherCredentials(string source, string target)
    {
        var result = Parse(["copy", "--source", source, "--target", target]);

        result.Options.Should().BeNull();
        result.Error.Should().Contain("name the same database");
    }

    [Fact]
    public void Parse_TheSameDatabaseNameOnAnotherServer_IsAllowed()
    {
        var result = Parse(["--source", "Host=a;Database=geoassets", "--target", "Host=b;Database=geoassets"]);

        result.Error.Should().BeNull();
        result.Options.Should().NotBeNull();
    }

    [Fact]
    public void Parse_NullArguments_Throw()
    {
        var act = () => DataMoveCommandLine.Parse(null!, null, null);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DatabaseOf_DescribesTheServerAndDatabaseOnly()
    {
        DataMoveCommandLine.DatabaseOf(Source).Should().Be("kernel-db:5432/geoassets");
        DataMoveCommandLine.DatabaseOf("garbage").Should().BeNull();
    }
}
