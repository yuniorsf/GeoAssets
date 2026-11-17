using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using Xunit;
using static GeoAssets.Tools.SharedUseImport.Tests.SharedUseTestData;

namespace GeoAssets.Tools.SharedUseImport.Tests;

public sealed class SharedUseReconciliationTests
{
    private static readonly SharedUseImportPlan MixedPlan = Plan(
        Row(1, pole: 1, count: 7),                                      // pole-1: over capacity
        Row(2, pole: 2, occupant: UnknownOccupant, wireType: UnmappedWireType), // pole-2: two flags on one row
        Row(3, pole: 2),
        Row(4, pole: 3, count: 6),                                      // pole-3: full
        Row(4, pole: 3),                                                // duplicate
        Row(5, pole: 404),                                              // unmapped pole
        Row(6, pole: 1, count: 0));                                     // no points

    [Fact]
    public void Build_CountsRowsEntriesPolesAndEveryReason_PerPoleAndInTotal()
    {
        var outcomes = new Dictionary<string, SharedUsePoleOutcome>(StringComparer.Ordinal)
        {
            ["pole-1"] = SharedUsePoleOutcome.Created,
            ["pole-2"] = SharedUsePoleOutcome.Unchanged,
            ["pole-3"] = SharedUsePoleOutcome.ExistingOccupancyDiffers,
        };

        var report = SharedUseReconciliation.Build(MixedPlan, outcomes, dryRun: false, Now);

        report.GeneratedAt.Should().Be(Now);
        report.DryRun.Should().BeFalse();
        report.OwnerOrganizationId.Should().Be(Owner);
        report.SkippedRows.Should().BeSameAs(MixedPlan.SkippedRows);
        report.Poles.Should().BeEquivalentTo(new[]
        {
            new SharedUsePoleReport("pole-1", SharedUsePoleOutcome.Created, OccupancyClass.OverCapacity, true, 1, 7, 6, 1, 7, [1], [SharedUsePoleFlag.OverCapacity], []),
            new SharedUsePoleReport("pole-2", SharedUsePoleOutcome.Unchanged, OccupancyClass.Unidentified, true, 2, 2, 2, 0, 2, [2], [SharedUsePoleFlag.UnidentifiedOccupant],
                [new FlaggedSharedUseRow(2, 3, SharedUseRowFlag.UnknownOccupant), new FlaggedSharedUseRow(2, 3, SharedUseRowFlag.UnmappedWireType)]),
            new SharedUsePoleReport("pole-3", SharedUsePoleOutcome.ExistingOccupancyDiffers, OccupancyClass.High, false, 1, 6, 6, 0, 6, [3], [], []),
        }, options => options.WithStrictOrdering());

        report.Totals.Should().BeEquivalentTo(new SharedUseImportTotals
        {
            RowsRead = 7,
            RowsAccepted = 4,
            RowsSkipped = 3,
            RowsImported = 3,
            RowsFlagged = 1,
            EntriesPlanned = 15,
            EntriesImported = 9,
            RegulatedEntriesPlanned = 14,
            OverCapacityEntriesPlanned = 1,
            Poles = 3,
            PolesImported = 2,
            PolesIrregular = 2,
            PolesFlagged = 2,
            SkippedRowsByReason = new Dictionary<string, int>(StringComparer.Ordinal) { ["DuplicateRow"] = 1, ["NonPositiveCount"] = 1, ["UnmappedPole"] = 1 },
            FlaggedRowsByFlag = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["OccupantWithoutCnpj"] = 0,
                ["UnidentifiedOccupant"] = 0,
                ["UnknownOccupant"] = 1,
                ["UnmappedWireType"] = 1,
                ["UnmatchedCnpj"] = 0,
            },
            PolesByFlag = new Dictionary<string, int>(StringComparer.Ordinal) { ["MergedLegacyPoles"] = 0, ["OverCapacity"] = 1, ["UnidentifiedOccupant"] = 1 },
            PolesByClass = new Dictionary<string, int>(StringComparer.Ordinal) { ["High"] = 1, ["Low"] = 0, ["None"] = 0, ["OverCapacity"] = 1, ["Unidentified"] = 1 },
            PolesByOutcome = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["ConcurrentWrite"] = 0,
                ["Created"] = 1,
                ["ExistingOccupancyDiffers"] = 1,
                ["Unchanged"] = 1,
                ["WouldCreate"] = 0,
            },
        });
        report.Totals.PolesByOutcome.Keys.Should().BeInAscendingOrder(StringComparer.Ordinal);
    }

    [Fact]
    public void Build_PoleFlaggedOnlyThroughARowFlag_CountsAsFlagged()
    {
        var plan = Plan(Row(1, wireType: UnmappedWireType));

        var report = SharedUseReconciliation.Build(plan, new Dictionary<string, SharedUsePoleOutcome>(StringComparer.Ordinal) { ["pole-1"] = SharedUsePoleOutcome.WouldCreate }, dryRun: true, Now);

        report.DryRun.Should().BeTrue();
        report.Totals.PolesFlagged.Should().Be(1);
        report.Totals.PolesIrregular.Should().Be(0);
        report.Totals.RowsImported.Should().Be(0, "a dry run imports nothing");
        report.Totals.RowsAccepted.Should().Be(1);
    }

    [Fact]
    public void Build_EmptyPlan_ReportsZerosForEveryKey()
    {
        var report = SharedUseReconciliation.Build(Plan(), new Dictionary<string, SharedUsePoleOutcome>(StringComparer.Ordinal), dryRun: false, Now);

        report.Poles.Should().BeEmpty();
        report.Totals.PolesByClass.Should().HaveCount(Enum.GetValues<OccupancyClass>().Length).And.OnlyContain(pair => pair.Value == 0);
        report.Totals.SkippedRowsByReason.Should().HaveCount(3).And.OnlyContain(pair => pair.Value == 0);
    }

    [Fact]
    public void Build_PoleWithoutOutcome_Throws()
    {
        var act = () => SharedUseReconciliation.Build(Plan(Row(1)), new Dictionary<string, SharedUsePoleOutcome>(StringComparer.Ordinal), dryRun: false, Now);

        act.Should().Throw<ArgumentException>().WithMessage("Pole 'pole-1' has no outcome.*");
    }

    [Fact]
    public void Build_NullArguments_Throw()
    {
        var nullPlan = () => SharedUseReconciliation.Build(null!, new Dictionary<string, SharedUsePoleOutcome>(StringComparer.Ordinal), false, Now);
        var nullOutcomes = () => SharedUseReconciliation.Build(Plan(), null!, false, Now);

        nullPlan.Should().Throw<ArgumentNullException>();
        nullOutcomes.Should().Throw<ArgumentNullException>();
    }
}
