using FluentAssertions;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Runs the shared <see cref="PoleOccupancyStoreContractTests"/> against
/// <see cref="EFPoleOccupancyRepository"/> on a real PostgreSQL (Testcontainers) — the engine
/// production runs on, and the one where text ordering, <c>numeric</c> precision,
/// <c>timestamp with time zone</c>, <c>ON CONFLICT</c> and row locking can differ from SQLite's.
/// The facts below it are the ones that need two real database sessions.
/// </summary>
public sealed class PostgresPoleOccupancyRepositoryContractTests : PoleOccupancyStoreContractTests
{
    private const string DatabaseName = "pole_occupancy_contract";

    private static readonly Guid Owner = PoleOccupancyEntryBuilder.DefaultPoleOwnerOrganizationId;
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    protected override async Task<PoleOccupancyStoreContractHarness> CreateHarnessAsync()
    {
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        var scope = new DisposableGroup();
        var context = scope.Add(PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Owner)));
        await context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE field_pole_occupancies CASCADE;");
        var clock = new FakeTimeProvider(Now);
        var store = new EFPoleOccupancyRepository(context, clock);

        return new PoleOccupancyStoreContractHarness(
            store,
            store,
            Owner,
            clock,
            organizationId =>
            {
                var view = new EFPoleOccupancyRepository(
                    scope.Add(PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(organizationId))), clock);
                return new PoleOccupancyStoreView(view, view);
            },
            scope);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_SecondConnectionHoldingTheVersionTheFirstAlreadyUsed_ThrowsOccupancyConflict_AndTheFirstWriteStands()
    {
        await using var harness = await CreateHarnessAsync();
        var seeded = await harness.Writer.ReplaceEntriesAsync("pole-1", 0, [Entry("pole-1", 1)], CancellationToken.None);
        var (writerA, writerB) = (harness.AsOrganization(Owner), harness.AsOrganization(Owner));

        // Both sessions read the pole at version 1 before either writes.
        var readByA = await writerA.Reader.GetForPoleAsync("pole-1", CancellationToken.None);
        var readByB = await writerB.Reader.GetForPoleAsync("pole-1", CancellationToken.None);
        var writtenByA = await writerA.Writer.ReplaceEntriesAsync(
            "pole-1", readByA!.Version, [.. readByA.Entries, Entry("pole-1", 2)], CancellationToken.None);
        var act = () => writerB.Writer.ReplaceEntriesAsync(
            "pole-1", readByB!.Version, [.. readByB.Entries, Entry("pole-1", 3), Entry("pole-1", 4)], CancellationToken.None);

        seeded.Version.Should().Be(1);
        (await act.Should().ThrowAsync<OccupancyConflictException>()).Which.PoleFeatureIds.Should().Equal("pole-1");
        var stored = await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None);
        stored.Should().BeEquivalentTo(writtenByA);
        stored!.Version.Should().Be(2);
        stored.Entries.Select(entry => entry.SlotIndex).Should().Equal(1, 2);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_ManyConnectionsRacingOnOneVersion_ExactlyOneWins_AndTheStoredEntriesAreThatWritersAlone()
    {
        const int Writers = 8;
        await using var harness = await CreateHarnessAsync();
        await harness.Writer.ReplaceEntriesAsync("pole-1", 0, [Entry("pole-1", 1)], CancellationToken.None);
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, Writers).Select(async writer =>
        {
            await using var context = PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Owner));
            await context.Database.OpenConnectionAsync();
            // Each writer stores a different number of over-capacity entries, so the winner is recognizable.
            var entries = Enumerable.Range(0, writer + 1).Select(_ => Entry("pole-1", slotIndex: null)).ToList();
            await start.Task;
            try
            {
                await new EFPoleOccupancyRepository(context).ReplaceEntriesAsync("pole-1", 1, entries, CancellationToken.None);
                return entries.Count;
            }
            catch (OccupancyConflictException)
            {
                return 0;
            }
        }).ToList();
        start.SetResult();
        var outcomes = await Task.WhenAll(attempts);

        outcomes.Count(written => written > 0).Should().Be(1);
        var stored = await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None);
        stored!.Version.Should().Be(2);
        stored.Entries.Should().HaveCount(outcomes.Single(written => written > 0), "the losers' entries must not be mixed into the winner's");
    }

    [Fact]
    public async Task ReplaceEntriesAsync_ConnectionsRacingToCreateTheSamePole_ExactlyOneCreatesIt()
    {
        const int Writers = 8;
        await using var harness = await CreateHarnessAsync();
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var attempts = Enumerable.Range(0, Writers).Select(async writer =>
        {
            await using var context = PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Owner));
            await context.Database.OpenConnectionAsync();
            var entries = Enumerable.Range(0, writer + 1).Select(_ => Entry("pole-new", slotIndex: null)).ToList();
            await start.Task;
            try
            {
                await new EFPoleOccupancyRepository(context).ReplaceEntriesAsync("pole-new", PoleOccupancy.UnsavedVersion, entries, CancellationToken.None);
                return entries.Count;
            }
            catch (OccupancyConflictException)
            {
                return 0;
            }
        }).ToList();
        start.SetResult();
        var outcomes = await Task.WhenAll(attempts);

        outcomes.Count(written => written > 0).Should().Be(1);
        var stored = await harness.Reader.GetForPoleAsync("pole-new", CancellationToken.None);
        stored!.Version.Should().Be(1);
        stored.Entries.Should().HaveCount(outcomes.Single(written => written > 0));
    }

    [Fact]
    public async Task ReplaceEntriesAsync_StoresTheStampAsUtcToTheMicrosecond_AndTheMeasurementsAsExactNumerics()
    {
        await using var harness = await CreateHarnessAsync();
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        harness.Clock.SetUtcNow(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero).AddTicks(1_234_567));
        var entry = new PoleOccupancyEntryBuilder().ForPole("pole-1").OwnedBy(Owner).OnSlot(1).WithCableDiameterMm(12.345m).WithCableLoadDaN(0.001m).Build();

        var written = await harness.Writer.ReplaceEntriesAsync("pole-1", 0, [entry], CancellationToken.None);

        written.UpdatedAt.Should().Be(new DateTimeOffset(2026, 6, 1, 9, 0, 0, TimeSpan.Zero).AddTicks(1_234_560), "sub-microsecond ticks are dropped, as PostgreSQL would");
        (await harness.Reader.GetForPoleAsync("pole-1", CancellationToken.None)).Should().BeEquivalentTo(written);
        await using var verify = PostgresTargetStoreDatabase.NewContext(connectionString);
        var raw = await verify.Database
            .SqlQueryRaw<string>("""SELECT ("CableDiameterMm"::text || '|' || "CableLoadDaN"::text || '|' || "Status") AS "Value" FROM field_pole_occupancy_entries""")
            .SingleAsync();
        raw.Should().Be("12.345|0.001|AuthorizedContracted");
    }

    private static PoleOccupancyEntry Entry(string poleFeatureId, int? slotIndex)
    {
        var builder = new PoleOccupancyEntryBuilder().ForPole(poleFeatureId).OwnedBy(Owner);
        return (slotIndex is { } index ? builder.OnSlot(index) : builder.BeyondRegulatedSlots()).Build();
    }
}
