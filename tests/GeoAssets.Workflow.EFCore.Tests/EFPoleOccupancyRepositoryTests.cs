using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Infrastructure.Observability;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Entities;
using GeoAssets.Workflow.TestKit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// What <see cref="EFPoleOccupancyRepository"/> does beyond the port contract (XD01-314):
/// transactions, the change tracker, the schema's own constraints and DI. The contract itself
/// runs in <see cref="EFPoleOccupancyRepositoryContractTests"/> and
/// <see cref="PostgresPoleOccupancyRepositoryContractTests"/>.
/// </summary>
public sealed class EFPoleOccupancyRepositoryTests : IDisposable
{
    private static readonly Guid Owner = PoleOccupancyEntryBuilder.DefaultPoleOwnerOrganizationId;
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly SqliteFixture _fixture = new(new FakeTenantContext(Owner));
    private readonly FakeTimeProvider _clock = new(Now);
    private readonly EFPoleOccupancyRepository _repository;

    public EFPoleOccupancyRepositoryTests()
    {
        _repository = new EFPoleOccupancyRepository(_fixture.Context, _clock);
    }

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public void Constructor_NullContext_ThrowsArgumentNullException()
    {
        var act = () => new EFPoleOccupancyRepository(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task Constructor_WithoutAClock_StampsWritesWithTheSystemClock()
    {
        var repository = new EFPoleOccupancyRepository(_fixture.Context);

        var written = await repository.ReplaceEntriesAsync("pole-1", 0, [Entry("pole-1", 1)], CancellationToken.None);

        written.UpdatedAt.Should().BeCloseTo(TimeProvider.System.GetUtcNow(), TimeSpan.FromMinutes(1));
        written.UpdatedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    // ── Transactions and the change tracker ───────────────────────────────────

    [Fact]
    public async Task ReplaceEntriesAsync_InsideTheCallersTransaction_IsRolledBackWithIt()
    {
        var first = await _repository.ReplaceEntriesAsync("pole-1", 0, [Entry("pole-1", 1)], CancellationToken.None);

        await using (var transaction = await _fixture.Context.Database.BeginTransactionAsync())
        {
            await _repository.ReplaceEntriesAsync("pole-1", first.Version, [Entry("pole-1", 1), Entry("pole-1", null)], CancellationToken.None);
            await _repository.ReplaceEntriesAsync("pole-2", 0, [Entry("pole-2", 1)], CancellationToken.None);
            (await _repository.GetForPoleAsync("pole-1", CancellationToken.None))!.Version.Should().Be(2, "the writer sees its own uncommitted write");
            await transaction.RollbackAsync();
        }

        (await _repository.GetForPoleAsync("pole-1", CancellationToken.None)).Should().BeEquivalentTo(first);
        (await _repository.GetForPoleAsync("pole-2", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ReplaceEntriesAsync_InsideTheCallersTransaction_IsCommittedWithIt_AndLeavesTheTransactionOpen()
    {
        await using var transaction = await _fixture.Context.Database.BeginTransactionAsync();

        await _repository.ReplaceEntriesAsync("pole-1", 0, [Entry("pole-1", 1)], CancellationToken.None);

        _fixture.Context.Database.CurrentTransaction.Should().BeSameAs(transaction);
        await transaction.CommitAsync();
        (await _repository.GetForPoleAsync("pole-1", CancellationToken.None))!.Version.Should().Be(1);
    }

    [Fact]
    public async Task ReplaceEntriesAsync_OnItsOwn_LeavesNoTransactionOpen_AndTracksNothing()
    {
        await _repository.ReplaceEntriesAsync("pole-1", 0, [Entry("pole-1", 1), Entry("pole-1", null)], CancellationToken.None);
        await _repository.GetForPoleAsync("pole-1", CancellationToken.None);

        _fixture.Context.Database.CurrentTransaction.Should().BeNull();
        _fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task ReplaceEntriesAsync_DoesNotSaveWhatElseTheSharedContextIsTracking()
    {
        var pending = new PoleOccupancyRecord { Id = Guid.NewGuid(), PoleFeatureId = "pole-pending", OrganizationId = Owner, Version = 1, UpdatedAt = Now.UtcDateTime };
        _fixture.Context.PoleOccupancies.Add(pending);

        await _repository.ReplaceEntriesAsync("pole-1", 0, [Entry("pole-1", 1)], CancellationToken.None);

        _fixture.Context.Entry(pending).State.Should().Be(EntityState.Added, "the store writes past the change tracker and never calls SaveChanges");
        (await _repository.GetForPoleAsync("pole-pending", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task ReplaceEntriesAsync_AConflictInsideItsOwnTransaction_LeavesNoTransactionOpen()
    {
        await _repository.ReplaceEntriesAsync("pole-1", 0, [Entry("pole-1", 1)], CancellationToken.None);
        await using var otherContext = _fixture.NewContext();
        var other = new EFPoleOccupancyRepository(otherContext, _clock);
        var readByOther = await other.GetForPoleAsync("pole-1", CancellationToken.None);
        await _repository.ReplaceEntriesAsync("pole-1", 1, [], CancellationToken.None);

        var act = () => other.ReplaceEntriesAsync("pole-1", readByOther!.Version, [Entry("pole-1", 2)], CancellationToken.None);

        await act.Should().ThrowAsync<OccupancyConflictException>();
        otherContext.Database.CurrentTransaction.Should().BeNull();
        (await _repository.GetForPoleAsync("pole-1", CancellationToken.None))!.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task ReplaceEntriesAsync_ManyOverCapacityEntries_AreInsertedInSeveralStatements_AndAllStored()
    {
        var entries = Enumerable.Range(0, (RawRowInsert.RowsPerStatement * 2) + 5).Select(_ => Entry("pole-1", null)).ToList();
        entries.AddRange(Enumerable.Range(1, 6).Select(slotIndex => Entry("pole-1", slotIndex)));

        await _repository.ReplaceEntriesAsync("pole-1", 0, entries, CancellationToken.None);

        var stored = await _repository.GetForPoleAsync("pole-1", CancellationToken.None);
        stored!.Entries.Should().HaveCount(211);
        stored.OverCapacityEntries.Should().HaveCount(205);
    }

    [Fact]
    public async Task GetForPolesAsync_MoreIdsThanOneInList_AreLookedUpInChunks()
    {
        await _repository.ReplaceEntriesAsync("pole-0000", 0, [Entry("pole-0000", 1)], CancellationToken.None);
        await _repository.ReplaceEntriesAsync("pole-1500", 0, [Entry("pole-1500", 1)], CancellationToken.None);
        var ids = Enumerable.Range(0, EFPoleOccupancyRepository.IdChunkSize + 600).Select(i => $"pole-{i:D4}").ToList();

        var poles = await _repository.GetForPolesAsync(ids, CancellationToken.None);
        var presences = await _repository.FindContractedOccupantsAsync(ids, CancellationToken.None);

        poles.Select(pole => pole.PoleFeatureId).Should().Equal("pole-0000", "pole-1500");
        presences.Should().ContainSingle().Which.PoleFeatureIds.Should().Equal("pole-0000", "pole-1500");
    }

    // ── The schema's own constraints (raw SQL, past the domain rules) ─────────

    [Fact]
    public async Task Schema_RejectsASecondEntryOnTheSameRegulatedSlot_ButNotASecondEntryWithoutASlot()
    {
        await _repository.ReplaceEntriesAsync("pole-1", 0, [Entry("pole-1", 3), Entry("pole-1", null)], CancellationToken.None);
        var poleId = await _fixture.Context.PoleOccupancies.Select(p => p.Id).SingleAsync();

        var duplicateSlot = () => InsertEntryRawAsync(poleId, slotIndex: 3);
        var secondWithoutSlot = () => InsertEntryRawAsync(poleId, slotIndex: null);

        (await duplicateSlot.Should().ThrowAsync<SqliteException>()).Which.Message.Should().Contain("UNIQUE");
        await secondWithoutSlot.Should().NotThrowAsync();
        (await _repository.GetForPoleAsync("pole-1", CancellationToken.None))!.OverCapacityEntries.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public async Task Schema_RejectsASlotIndexOutsideOneToSix(int slotIndex)
    {
        await _repository.ReplaceEntriesAsync("pole-1", 0, [], CancellationToken.None);
        var poleId = await _fixture.Context.PoleOccupancies.Select(p => p.Id).SingleAsync();

        var act = () => InsertEntryRawAsync(poleId, slotIndex);

        (await act.Should().ThrowAsync<SqliteException>()).Which.Message.Should().Contain("CHECK");
    }

    [Fact]
    public async Task Schema_RejectsASecondPoleRowForTheSameOwnerAndPole_AndAnEntryWithoutItsPoleOrUnderAnotherOrganization()
    {
        await _repository.ReplaceEntriesAsync("pole-1", 0, [], CancellationToken.None);
        var poleId = await _fixture.Context.PoleOccupancies.Select(p => p.Id).SingleAsync();
        var otherOrganization = () => InsertEntryRawAsync(poleId, slotIndex: 1, organizationId: Guid.NewGuid());

        var secondPole = () => _fixture.Context.Database.ExecuteSqlAsync(
            $"""INSERT INTO field_pole_occupancies ("Id", "PoleFeatureId", "OrganizationId", "Version", "UpdatedAt") VALUES ({Guid.NewGuid()}, 'pole-1', {Owner}, 1, {Now.UtcDateTime})""");
        var orphan = () => InsertEntryRawAsync(Guid.NewGuid(), slotIndex: 1);

        (await secondPole.Should().ThrowAsync<SqliteException>()).Which.Message.Should().Contain("UNIQUE");
        (await orphan.Should().ThrowAsync<SqliteException>()).Which.Message.Should().Contain("FOREIGN KEY");
        (await otherOrganization.Should().ThrowAsync<SqliteException>()).Which.Message.Should().Contain(
            "FOREIGN KEY", "an entry must carry the organization of its pole");
    }

    [Fact]
    public async Task Schema_DeletingAPoleRow_DeletesItsEntries()
    {
        await _repository.ReplaceEntriesAsync("pole-1", 0, [Entry("pole-1", 1), Entry("pole-1", null)], CancellationToken.None);
        await _repository.ReplaceEntriesAsync("pole-2", 0, [Entry("pole-2", 1)], CancellationToken.None);

        await _fixture.Context.Database.ExecuteSqlAsync($"""DELETE FROM field_pole_occupancies WHERE "PoleFeatureId" = 'pole-1'""");

        (await _fixture.Context.PoleOccupancyEntries.CountAsync()).Should().Be(1);
        (await _repository.GetForPoleAsync("pole-2", CancellationToken.None))!.Entries.Should().ContainSingle();
    }

    [Fact]
    public async Task GetForPoleAsync_AStoredStatusThatIsNoLongerKnown_FailsTheReadLoudly()
    {
        await _repository.ReplaceEntriesAsync("pole-1", 0, [Entry("pole-1", 1)], CancellationToken.None);
        await _fixture.Context.Database.ExecuteSqlAsync($"""UPDATE field_pole_occupancy_entries SET "Status" = 'Retired'""");

        var act = () => _repository.GetForPoleAsync("pole-1", CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("Retired");
    }

    // ── DI registration ───────────────────────────────────────────────────────

    [Fact]
    public void AddWorkflowPersistence_RegistersBothOccupancyPorts_AsOneScopedRepositoryPerScope()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<GeoAssetsActivitySource>();
        services.AddSingleton<GeoAssetsMeter>();
        services.AddWorkflowPersistence(options => options.UseSqlite("Data Source=:memory:"));
        using var provider = services.BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();
        using var otherScope = provider.CreateScope();
        var reader = scope.ServiceProvider.GetRequiredService<IPoleOccupancyReader>();
        var writer = scope.ServiceProvider.GetRequiredService<IPoleOccupancyWriter>();

        reader.Should().BeOfType<EFPoleOccupancyRepository>();
        writer.Should().BeSameAs(reader);
        otherScope.ServiceProvider.GetRequiredService<IPoleOccupancyReader>().Should().NotBeSameAs(reader);
    }

    [Fact]
    public void AddPoleOccupancyPersistence_CalledTwice_KeepsASingleRegistrationPerPort()
    {
        var services = new ServiceCollection();

        services.AddPoleOccupancyPersistence().AddPoleOccupancyPersistence();

        services.Count(d => d.ServiceType == typeof(IPoleOccupancyReader)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(IPoleOccupancyWriter)).Should().Be(1);
        services.Count(d => d.ServiceType == typeof(EFPoleOccupancyRepository)).Should().Be(1);
        services.Should().OnlyContain(d => d.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddPoleOccupancyPersistence_NullServices_ThrowsArgumentNullException()
    {
        var act = () => WorkflowEFCoreServiceExtensions.AddPoleOccupancyPersistence(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static PoleOccupancyEntry Entry(string poleFeatureId, int? slotIndex)
    {
        var builder = new PoleOccupancyEntryBuilder().ForPole(poleFeatureId).OwnedBy(Owner);
        return (slotIndex is { } index ? builder.OnSlot(index) : builder.BeyondRegulatedSlots()).Build();
    }

    private Task<int> InsertEntryRawAsync(Guid poleId, int? slotIndex, Guid? organizationId = null) =>
        _fixture.Context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO field_pole_occupancy_entries ("Id", "PoleOccupancyId", "SlotIndex", "Status", "CableDiameterMm", "CableLoadDaN", "OrganizationId")
            VALUES ({Guid.NewGuid()}, {poleId}, {slotIndex}, 'ClandestineUnauthorized', '1.0', '0.0', {organizationId ?? Owner})
            """);
}
