using FluentAssertions;
using Xunit;

namespace GeoAssets.Infrastructure.Observability.Tests;

/// <summary>
/// The approval saga's alert rules in <c>deploy/otel/alerts/saga-apply-alerts.yaml</c> (XD01-383, XD01-392): each rule names an
/// instrument the meter really publishes, in its Prometheus form.
/// </summary>
public sealed class SagaApplyAlertRulesTests
{
    [Fact]
    public void CompensationConflictRule_Exists_AndAlertsOnTheCompensationConflictsCounterByOrderType()
    {
        var rule = RuleBlock("GeoAssetsSagaCompensationConflict");

        rule.Should().Contain("geoassets_saga_apply_compensation_conflicts_total");
        rule.Should().Contain("increase(").And.Contain("> 0");
        rule.Should().Contain("sum by (order_type)");
        rule.Should().Contain("severity: warning");
        rule.Should().Contain("occupancy.apply.unrestored");
    }

    [Fact]
    public void EveryRuleOfTheFile_ReferencesAGeoAssetsSagaApplyMetric()
    {
        var text = File.ReadAllText(AlertsFile());

        var rules = text.Split("- alert: ", StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();

        rules.Should().HaveCountGreaterThanOrEqualTo(4);
        rules.Should().OnlyContain(rule => rule.Contains("geoassets_saga_apply_", StringComparison.Ordinal));
    }

    [Fact]
    public void TheCompensationConflictsMetric_NamedByTheRule_IsTheInstrumentTheMeterPublishes()
    {
        var instrument = "geoassets.saga.apply.compensation_conflicts";
        var prometheus = instrument.Replace('.', '_') + "_total";

        RuleBlock("GeoAssetsSagaCompensationConflict").Should().Contain(prometheus);
        File.ReadAllText(AlertsFile()).Should().Contain(instrument + "{order.type}", "the header documents the mapping from the instrument");
    }

    private static string RuleBlock(string alertName)
    {
        var text = File.ReadAllText(AlertsFile());
        var start = text.IndexOf("- alert: " + alertName, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, "the rule {0} is declared", alertName);
        var next = text.IndexOf("- alert: ", start + 1, StringComparison.Ordinal);
        return next < 0 ? text[start..] : text[start..next];
    }

    private static string AlertsFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the test runs under the repository");
        return Path.Combine(directory!.FullName, "deploy", "otel", "alerts", "saga-apply-alerts.yaml");
    }
}
