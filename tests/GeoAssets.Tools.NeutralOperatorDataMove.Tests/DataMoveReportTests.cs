using FluentAssertions;
using Npgsql;

namespace GeoAssets.Tools.NeutralOperatorDataMove.Tests;

public sealed class DataMoveReportTests
{
    private const string Password = "s3cr3t-p4ssw0rd";
    private const string Source = $"Host=kernel-db;Port=5432;Database=geoassets;Username=svc;Password={Password}";
    private const string Target = $"Host=no-db;Port=5432;Database=geoassets_neutral_operator;Username=svc;Password={Password}";

    private static readonly string[] _orderTypes = ["isp-clandestine-notice", "shared-use-pole-work"];

    private static DataMoveReport ReportOf(DataMoveVerb verb) =>
        new(verb, _orderTypes, DataMoveCommandLine.DatabaseOf(Source)!, DataMoveCommandLine.DatabaseOf(Target)!);

    private static TableOutcome Table(string name, long source = 0, long identical = 0, long missing = 0, long different = 0, long copied = 0, long removed = 0) =>
        new(name) { SourceRows = source, Identical = identical, Missing = missing, Different = different, Copied = copied, Removed = removed };

    // ── exit codes ────────────────────────────────────────────────────────

    [Fact]
    public void ExitCode_ForAnEmptyReport_IsSucceeded()
    {
        ReportOf(DataMoveVerb.Plan).ExitCode.Should().Be(DataMoveExitCodes.Succeeded).And.Be(0);
    }

    [Theory]
    [InlineData("Info", 0)]
    [InlineData("Warning", 0)]
    [InlineData("Conflict", 2)]
    [InlineData("Refusal", 3)]
    public void ExitCode_FollowsTheMostSevereIssue(string severityName, int expected)
    {
        var severity = Enum.Parse<DataMoveIssueSeverity>(severityName);
        var report = ReportOf(DataMoveVerb.Copy);
        report.Add(severity, "some.code", "a message");

        report.ExitCode.Should().Be(expected);
    }

    [Fact]
    public void ExitCode_ARefusalOutranksAConflict()
    {
        var report = ReportOf(DataMoveVerb.Copy);
        report.Add(DataMoveIssueSeverity.Conflict, "order.parent-not-moved", "x");
        report.Add(DataMoveIssueSeverity.Refusal, "rls.filtered", "y");
        report.SetTables([Table("public.ServiceOrders", different: 1)]);

        report.ExitCode.Should().Be(DataMoveExitCodes.Refused).And.Be(3);
    }

    [Theory]
    [InlineData("Plan")]
    [InlineData("Copy")]
    [InlineData("Verify")]
    [InlineData("Remove")]
    public void ExitCode_ADifferingRow_IsDifferencesFound_ForEveryVerb(string verbName)
    {
        var verb = Enum.Parse<DataMoveVerb>(verbName);
        var report = ReportOf(verb);
        report.SetTables([Table("public.ServiceOrders", source: 3, identical: 2, different: 1)]);

        report.ExitCode.Should().Be(DataMoveExitCodes.DifferencesFound).And.Be(2);
    }

    [Theory]
    [InlineData("Plan", 0)]
    [InlineData("Copy", 0)]
    [InlineData("Verify", 2)]
    public void ExitCode_AMissingRow_OnlyFailsVerify(string verbName, int expected)
    {
        var verb = Enum.Parse<DataMoveVerb>(verbName);
        var report = ReportOf(verb);
        report.SetTables([Table("public.ServiceOrders", source: 3, identical: 2, missing: 1)]);

        report.ExitCode.Should().Be(expected);
    }

    [Fact]
    public void ExitCodes_AreTheDocumentedOnes()
    {
        new[] { DataMoveExitCodes.Succeeded, DataMoveExitCodes.UsageError, DataMoveExitCodes.DifferencesFound, DataMoveExitCodes.Refused, DataMoveExitCodes.Failed }
            .Should().Equal(0, 1, 2, 3, 4);
        DataMoveOptions.Usage.Should().Contain("0 done, nothing wrong").And.Contain("1 usage error").And.Contain("2 conflicts or differences found")
            .And.Contain("3 refused").And.Contain("4 failed");
    }

    [Fact]
    public void SetTables_ReplacesThePreviousOutcomes()
    {
        var report = ReportOf(DataMoveVerb.Copy);
        report.SetTables([Table("a", different: 1)]);
        report.SetTables([Table("a", identical: 1)]);

        report.Tables.Should().ContainSingle();
        report.HasConflict.Should().BeFalse();
    }

    [Fact]
    public void TableOutcome_KeepsOnlyTheFirstSamplesOfDifferingKeys_ButCountsAll()
    {
        var outcome = new TableOutcome("public.ServiceOrders");
        for (var i = 0; i < TableOutcome.MaxSamples + 5; i++) outcome.AddDifferent(1, $"order-{i}");

        outcome.Different.Should().Be(TableOutcome.MaxSamples + 5);
        outcome.DifferentKeys.Should().HaveCount(TableOutcome.MaxSamples).And.StartWith("order-0");
    }

    // ── rendering ─────────────────────────────────────────────────────────

    [Fact]
    public void Render_ShowsTheDatabasesAsHostPortAndName_NeverCredentials()
    {
        var report = ReportOf(DataMoveVerb.Copy);
        report.SetTables([Table("public.ServiceOrders", source: 6, missing: 6)]);
        report.Add(DataMoveIssueSeverity.Warning, "schema.modifier-mismatch", "a message");

        var text = DataMoveReportWriter.Render(report);

        text.Should().Contain("kernel-db:5432/geoassets").And.Contain("no-db:5432/geoassets_neutral_operator");
        text.Should().NotContain(Password).And.NotContain("svc").And.NotContain("Password").And.NotContain("Username");
    }

    [Fact]
    public void Options_ToString_AndUsage_NeverCarryTheConnectionStrings()
    {
        var options = new DataMoveOptions { Verb = DataMoveVerb.Copy, SourceConnectionString = Source, TargetConnectionString = Target, IncludePendingOutbox = true };

        options.ToString().Should().Be("Copy (batch size 500, pending outbox included)");
        options.ToString().Should().NotContain(Password);
        DataMoveOptions.Usage.Should().NotContain(Password).And.NotContain("Password=");
    }

    [Fact]
    public void Render_ListsDifferingKeys_AndIssuesMostSevereFirst()
    {
        var report = ReportOf(DataMoveVerb.Verify);
        var table = Table("public.ServiceOrders", source: 2);
        table.AddDifferent(1, "no-created");
        report.SetTables([table]);
        report.Add(DataMoveIssueSeverity.Info, "outbox.kernel-answers", "info message");
        report.Add(DataMoveIssueSeverity.Refusal, "rls.filtered", "refusal message");

        var text = DataMoveReportWriter.Render(report);

        text.Should().Contain("differs: no-created");
        text.IndexOf("REFUSAL", StringComparison.Ordinal).Should().BeLessThan(text.IndexOf("INFO", StringComparison.Ordinal));
        text.Should().Contain("Result: exit 3");
    }

    [Theory]
    [InlineData("Plan", "plan (dry run, nothing written)", "ready")]
    [InlineData("Verify", "verify (nothing written)", "every moved row is in the target, identical")]
    [InlineData("Remove", "remove", "removed from the kernel's database")]
    public void Render_ANormalRun_SaysWhatTheVerbDid(string verbName, string title, string outcome)
    {
        var verb = Enum.Parse<DataMoveVerb>(verbName);
        var text = DataMoveReportWriter.Render(ReportOf(verb));

        text.Should().Contain($"NeutralOperator data move: {title}").And.Contain($"Result: exit 0 — {outcome}");
        text.Should().Contain("order types moved: isp-clandestine-notice, shared-use-pole-work");
    }

    [Fact]
    public void Render_ACopyThatChangedNothing_SaysTheTargetAlreadyHoldsEveryRow()
    {
        var text = DataMoveReportWriter.Render(ReportOf(DataMoveVerb.Copy));

        text.Should().Contain("nothing to copy (the target already holds every row)");
    }

    [Fact]
    public void Render_ACopyThatChangedRows_SaysCopied()
    {
        var report = ReportOf(DataMoveVerb.Copy);
        report.Changed = true;

        DataMoveReportWriter.Render(report).Should().Contain("Result: exit 0 — copied");
    }

    [Theory]
    [InlineData(false, "refused; nothing was changed")]
    [InlineData(true, "refused part-way")]
    public void Render_ARefusal_SaysWhetherAnythingChanged(bool changed, string expected)
    {
        var report = ReportOf(DataMoveVerb.Copy);
        report.Changed = changed;
        report.Add(DataMoveIssueSeverity.Refusal, "migrations.service-orders", "x");

        DataMoveReportWriter.Render(report).Should().Contain(expected);
    }

    [Fact]
    public void Render_AConflictingCopy_SaysNothingWasOverwritten()
    {
        var report = ReportOf(DataMoveVerb.Copy);
        report.Add(DataMoveIssueSeverity.Conflict, "copy.stopped", "x");

        DataMoveReportWriter.Render(report).Should().Contain("conflicts or differences found; nothing was overwritten");

        report.Changed = true;
        DataMoveReportWriter.Render(report).Should().Contain("copied what it could; conflicts found (nothing was overwritten)");
    }

    // ── the application: output, errors, exit codes ───────────────────────

    private static async Task<(int Exit, string Output, string Error)> RunAsync(
        Func<DataMoveOptions, CancellationToken, Task<DataMoveReport>> run, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await new DataMoveApplication(run, output, error).RunAsync(args, null, null, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }

    private static readonly string[] _args = ["--source", Source, "--target", Target];

    [Fact]
    public async Task Application_PrintsTheReportAndAnswersItsExitCode_WithoutCredentials()
    {
        var (exit, output, error) = await RunAsync(
            (options, _) =>
            {
                var report = ReportOf(options.Verb);
                report.Add(DataMoveIssueSeverity.Refusal, "rls.filtered", "Row-level security would filter public.ServiceOrders.");
                return Task.FromResult(report);
            },
            ["copy", .. _args]);

        exit.Should().Be(3);
        output.Should().Contain("rls.filtered").And.NotContain(Password);
        error.Should().BeEmpty();
    }

    [Fact]
    public async Task Application_PassesTheParsedOptionsToTheRun()
    {
        DataMoveOptions? seen = null;

        var (exit, _, _) = await RunAsync(
            (options, _) =>
            {
                seen = options;
                return Task.FromResult(ReportOf(options.Verb));
            },
            ["verify", "--batch-size", "7", .. _args]);

        exit.Should().Be(0);
        seen!.Verb.ToString().Should().Be("Verify");
        seen.BatchSize.Should().Be(7);
        seen.SourceConnectionString.Should().Be(Source);
    }

    [Fact]
    public async Task Application_AUsageError_PrintsTheErrorAndUsage_ExitsOne_AndNeverRuns()
    {
        var ran = false;

        var (exit, output, error) = await RunAsync(
            (options, _) =>
            {
                ran = true;
                return Task.FromResult(ReportOf(options.Verb));
            },
            ["remove", .. _args]);

        exit.Should().Be(1);
        ran.Should().BeFalse("no database is opened for a usage error");
        output.Should().BeEmpty();
        error.Should().Contain("pass --confirm").And.Contain("Usage: GeoAssets.Tools.NeutralOperatorDataMove").And.NotContain(Password);
    }

    [Fact]
    public async Task Application_Help_PrintsTheUsage_AndExitsZero()
    {
        var (exit, output, error) = await RunAsync((_, _) => throw new InvalidOperationException("must not run"), "--help");

        exit.Should().Be(0);
        output.Should().Contain("Usage: GeoAssets.Tools.NeutralOperatorDataMove").And.Contain("--include-pending-outbox").And.Contain("--confirm");
        error.Should().BeEmpty();
    }

    [Theory]
    [InlineData("db")]
    [InlineData("invalid")]
    public async Task Application_ADatabaseOrInvalidOperationFailure_IsExitFour_ReportedByMessageOnly(string kind)
    {
        Exception failure = string.Equals(kind, "db", StringComparison.Ordinal) ? new NpgsqlException("connection refused.") : new InvalidOperationException("The EF model no longer maps x.");

        var (exit, output, error) = await RunAsync((_, _) => throw failure, _args);

        exit.Should().Be(DataMoveExitCodes.Failed).And.Be(4);
        output.Should().BeEmpty();
        error.Should().Contain("Failed: ").And.Contain("Every open transaction was rolled back").And.NotContain(Password);
        error.Should().NotContain("   at ", "no stack trace is printed");
    }

    [Fact]
    public async Task Application_ATimeout_IsExitFour()
    {
        var (exit, _, error) = await RunAsync((_, _) => throw new TimeoutException("Timeout during connection attempt."), _args);

        exit.Should().Be(4);
        error.Should().Contain("Timeout during connection attempt");
    }

    [Fact]
    public async Task Application_ACancellation_IsExitFour_AndSaysItRolledBack()
    {
        var (exit, _, error) = await RunAsync((_, _) => throw new OperationCanceledException(), _args);

        exit.Should().Be(4);
        error.Should().Contain("Cancelled; every open transaction was rolled back.");
    }

    [Fact]
    public Task Application_AnUnexpectedException_IsNotSwallowed()
    {
        Func<Task> act = () => RunAsync((_, _) => throw new NotSupportedException("a bug"), _args);

        return act.Should().ThrowAsync<NotSupportedException>();
    }
}
