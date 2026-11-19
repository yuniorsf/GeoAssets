using FluentAssertions;
using GeoAssets.NeutralOperator.Domain;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static GeoAssets.Tools.SharedUseImport.Tests.SharedUseTestData;

namespace GeoAssets.Tools.SharedUseImport.Tests;

public sealed class SharedUseImporterTests
{
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly InMemoryPoleOccupancyStore _store;

    public SharedUseImporterTests() => _store = new InMemoryPoleOccupancyStore(new FixedTenantContext(Owner), _clock);

    private SharedUseImporter Importer(IPoleOccupancyReader? reader = null, IPoleOccupancyWriter? writer = null) =>
        new(reader ?? _store, writer ?? _store, _clock);

    private static readonly SharedUseImportPlan OverCapacityPlan = Plan(
        Row(1, pole: 1, count: 4), Row(2, pole: 1, occupant: LegacySharedUseRow.UnidentifiedOccupantId, count: 5), Row(3, pole: 2));

    [Fact]
    public async Task ImportAsync_PolesNotOnFile_AreWritten_OverCapacityEntriesIncluded()
    {
        _clock.Advance(TimeSpan.FromMinutes(5));

        var report = await Importer().ImportAsync(OverCapacityPlan, dryRun: false, CancellationToken.None);

        report.Poles.Select(pole => pole.Outcome).Should().AllBeEquivalentTo(SharedUsePoleOutcome.Created);
        report.GeneratedAt.Should().Be(Now.AddMinutes(5));
        var stored = await _store.GetForPoleAsync("pole-1", CancellationToken.None);
        stored!.Version.Should().Be(PoleOccupancy.FirstVersion);
        stored.Entries.Should().Equal(OverCapacityPlan.Poles[0].Occupancy.Entries);
        stored.OverCapacityEntries.Should().HaveCount(3);
        stored.Class.Should().Be(OccupancyClass.Unidentified);
        stored.IsOverCapacity.Should().BeTrue();
        (await _store.GetForPoleAsync("pole-2", CancellationToken.None))!.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task ImportAsync_SameInputAgain_ChangesNothing_AndReportsTheSameReconciliation()
    {
        var first = await Importer().ImportAsync(OverCapacityPlan, dryRun: false, CancellationToken.None);
        var storedAfterFirst = _store.Snapshot();

        _clock.Advance(TimeSpan.FromHours(1));
        var replanned = SharedUseImportPlanner.Plan(
            [Row(3, pole: 2), Row(2, pole: 1, occupant: LegacySharedUseRow.UnidentifiedOccupantId, count: 5), Row(1, pole: 1, count: 4)],
            Mapping(), Owner, _clock.GetUtcNow());
        var second = await Importer().ImportAsync(replanned, dryRun: false, CancellationToken.None);

        second.Poles.Select(pole => pole.Outcome).Should().AllBeEquivalentTo(SharedUsePoleOutcome.Unchanged);
        _store.Snapshot().Should().BeEquivalentTo(storedAfterFirst, "an unchanged pole is not written again, so neither its version nor its stamp moves");
        second.Poles.Should().BeEquivalentTo(first.Poles, options => options.Excluding(pole => pole.Outcome));
        second.Totals.Should().BeEquivalentTo(first.Totals, options => options.Excluding(totals => totals.PolesByOutcome));
        second.Totals.PolesImported.Should().Be(first.Totals.PolesImported);
        second.Totals.RowsImported.Should().Be(first.Totals.RowsImported);
    }

    [Fact]
    public async Task ImportAsync_DryRun_WritesNothing_AndSaysWhatItWouldDo()
    {
        var dryRun = await Importer().ImportAsync(OverCapacityPlan, dryRun: true, CancellationToken.None);

        dryRun.DryRun.Should().BeTrue();
        dryRun.Poles.Select(pole => pole.Outcome).Should().AllBeEquivalentTo(SharedUsePoleOutcome.WouldCreate);
        _store.Snapshot().Should().BeEmpty();

        await Importer().ImportAsync(OverCapacityPlan, dryRun: false, CancellationToken.None);
        var afterImport = await Importer().ImportAsync(OverCapacityPlan, dryRun: true, CancellationToken.None);

        afterImport.Poles.Select(pole => pole.Outcome).Should().AllBeEquivalentTo(SharedUsePoleOutcome.Unchanged);
    }

    [Fact]
    public async Task ImportAsync_PoleAlreadyHoldingOtherOccupancy_IsNeverOverwritten()
    {
        var existing = new PoleOccupancyEntryBuilder().ForPole("pole-2").OwnedBy(Owner).OnSlot(3)
            .WithStatus(PoleAttachmentOccupantStatus.AuthorizedContracted).OccupiedBy(OccupantOrganization).Build();
        var written = await _store.ReplaceEntriesAsync("pole-2", PoleOccupancy.UnsavedVersion, [existing], CancellationToken.None);

        var report = await Importer().ImportAsync(OverCapacityPlan, dryRun: false, CancellationToken.None);

        report.Poles.Select(pole => (pole.PoleFeatureId, pole.Outcome)).Should().Equal(
            ("pole-1", SharedUsePoleOutcome.Created),
            ("pole-2", SharedUsePoleOutcome.ExistingOccupancyDiffers));
        (await _store.GetForPoleAsync("pole-2", CancellationToken.None)).Should().BeEquivalentTo(written);
    }

    [Fact]
    public async Task ImportAsync_PoleEmptiedOnFile_IsNotRefilled()
    {
        await _store.ReplaceEntriesAsync("pole-2", PoleOccupancy.UnsavedVersion, [], CancellationToken.None);

        var report = await Importer().ImportAsync(Plan(Row(3, pole: 2)), dryRun: false, CancellationToken.None);

        report.Poles.Single().Outcome.Should().Be(SharedUsePoleOutcome.ExistingOccupancyDiffers);
        (await _store.GetForPoleAsync("pole-2", CancellationToken.None))!.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ImportAsync_LosingTheRaceToCreateAPole_ReportsAConcurrentWrite_AndCarriesOn()
    {
        var report = await Importer(writer: new ConflictingPoleOccupancyWriter()).ImportAsync(OverCapacityPlan, dryRun: false, CancellationToken.None);

        report.Poles.Select(pole => pole.Outcome).Should().AllBeEquivalentTo(SharedUsePoleOutcome.ConcurrentWrite);
        report.Totals.PolesImported.Should().Be(0);
        report.Totals.PolesByOutcome["ConcurrentWrite"].Should().Be(2);
    }

    [Fact]
    public async Task ImportAsync_ReadsTheStoreInBatches()
    {
        var poleCount = SharedUseImporter.ReadBatchSize + 1;
        var mapping = Mapping(Document(poles: [.. Enumerable.Range(1, poleCount).Select(pole => new PoleMapping(pole, $"pole-{pole:D4}"))]));
        var plan = SharedUseImportPlanner.Plan([.. Enumerable.Range(1, poleCount).Select(pole => Row(pole, pole: pole))], mapping, Owner, Now);
        var reader = new RecordingPoleOccupancyReader(_store);

        var report = await Importer(reader: reader).ImportAsync(plan, dryRun: false, CancellationToken.None);

        reader.BatchSizes.Should().Equal(SharedUseImporter.ReadBatchSize, 1);
        report.Totals.PolesImported.Should().Be(poleCount);
        _store.Snapshot().Should().HaveCount(poleCount);
    }

    [Fact]
    public async Task ImportAsync_CancelledToken_ThrowsBeforeAnything()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var reader = new RecordingPoleOccupancyReader(_store);

        var act = () => Importer(reader: reader).ImportAsync(OverCapacityPlan, dryRun: false, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        reader.BatchSizes.Should().BeEmpty();
        _store.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public Task ImportAsync_NullPlan_Throws()
    {
        var act = () => Importer().ImportAsync(null!, dryRun: false, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullArguments_Throw()
    {
        var nullReader = () => new SharedUseImporter(null!, _store, _clock);
        var nullWriter = () => new SharedUseImporter(_store, null!, _clock);
        var nullClock = () => new SharedUseImporter(_store, _store, null!);

        nullReader.Should().Throw<ArgumentNullException>().WithParameterName("reader");
        nullWriter.Should().Throw<ArgumentNullException>().WithParameterName("writer");
        nullClock.Should().Throw<ArgumentNullException>().WithParameterName("timeProvider");
    }
}
