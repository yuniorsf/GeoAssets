using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Runs the shared <see cref="OccupancyChangeSetStoreContractTests"/> against
/// <see cref="EFOccupancyChangeSetRepository"/> on a real PostgreSQL (Testcontainers) — the
/// engine production runs on, and the one where text and <c>uuid</c> ordering, the partial
/// unique index, <c>ON CONFLICT DO NOTHING</c> and row visibility between sessions can differ
/// from SQLite's. The facts below it are the ones that need several real database sessions.
/// </summary>
public sealed class PostgresOccupancyChangeSetRepositoryContractTests : OccupancyChangeSetStoreContractTests
{
    private const string DatabaseName = "occupancy_change_set_contract";

    private static readonly Guid Owner = ServiceOrderTargetBuilder.DefaultOrganizationId;
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 9, 0, 0, TimeSpan.Zero);

    protected override async Task<OccupancyChangeSetStoreContractHarness> CreateHarnessAsync()
    {
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        await PostgresTargetStoreDatabase.ClearAsync(connectionString);

        var scope = new DisposableGroup();
        var context = scope.Add(PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Owner)));
        var orderTypes = new OrderTypeRegistry();
        var clock = new FakeTimeProvider(Now);
        var store = new EFOccupancyChangeSetRepository(context, clock);

        return new OccupancyChangeSetStoreContractHarness(
            store,
            store,
            Owner,
            clock,
            new EFServiceOrderRepository(context, orderTypeRegistry: orderTypes, timeProvider: clock, hooks: [new OccupancyReleaseLifecycleHook(store)]),
            new EFServiceOrderTargetRepository(context, orderTypes, clock),
            organizationId =>
            {
                var view = new EFOccupancyChangeSetRepository(
                    scope.Add(PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(organizationId))), clock);
                return new OccupancyChangeSetStoreView(view, view);
            },
            scope);
    }

    [Fact]
    public async Task StageAsync_TwoConnectionsStagingForTheSameTarget_BothSucceed_AndTheLaterSetIsTheOneStaged()
    {
        await using var harness = await CreateHarnessAsync();
        var target = await SeedTargetAsync(harness, "ord-1", "pole-1");
        var (sessionA, sessionB) = (harness.AsOrganization(Owner), harness.AsOrganization(Owner));
        var setA = NewSet(harness, "ord-1", "pole-1", [OccupancyChangeBuilder.Add(1)], stagedBy: "crew-a");
        var setB = NewSet(harness, "ord-1", "pole-1", [OccupancyChangeBuilder.Add(2), OccupancyChangeBuilder.Add(3)], stagedBy: "crew-b");

        // Both sessions see "nothing staged" before either writes.
        (await sessionA.Reader.GetStagedAsync("ord-1", target.Id, CancellationToken.None)).Should().BeNull();
        (await sessionB.Reader.GetStagedAsync("ord-1", target.Id, CancellationToken.None)).Should().BeNull();
        await sessionA.Writer.StageAsync(setA, CancellationToken.None);
        await sessionB.Writer.StageAsync(setB, CancellationToken.None);

        var staged = await harness.Reader.GetStagedAsync("ord-1", target.Id, CancellationToken.None);
        staged.Should().BeEquivalentTo(setB);
        var all = await harness.Reader.GetPageAsync(new OccupancyChangeSetQuery("ord-1"), CancellationToken.None);
        all.Items.Should().ContainSingle("the earlier session's set was replaced, not kept beside it");
    }

    [Fact]
    public async Task StageAsync_ManyConnectionsRacingToStageForOneTarget_EveryCallSucceeds_AndExactlyOneWholeSetRemainsStaged()
    {
        const int Writers = 8;
        await using var harness = await CreateHarnessAsync();
        var target = await SeedTargetAsync(harness, "ord-1", "pole-1");
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // Each writer stages a different number of changes under its own name, so the surviving
        // set is recognizable and a mix of two writers' data would show.
        var sets = Enumerable.Range(0, Writers)
            .Select(writer => NewSet(
                harness, "ord-1", "pole-1",
                [.. Enumerable.Range(0, writer + 1).Select(_ => OccupancyChangeBuilder.AddBeyondRegulatedSlots())],
                stagedBy: $"crew-{writer}"))
            .ToList();
        var attempts = sets.Select(async set =>
        {
            await using var context = PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Owner));
            await context.Database.OpenConnectionAsync();
            await start.Task;
            return await new EFOccupancyChangeSetRepository(context).StageAsync(set, CancellationToken.None);
        }).ToList();
        start.SetResult();
        var stored = await Task.WhenAll(attempts);

        stored.Select(set => set.Id).Should().BeEquivalentTo(sets.Select(set => set.Id), "no call failed");
        await using var verify = PostgresTargetStoreDatabase.NewContext(connectionString);
        var rows = await verify.Database
            .SqlQuery<int>($"""SELECT COUNT(*)::int AS "Value" FROM "ServiceOrderOccupancyChangeSets" WHERE "TargetId" = {target.Id}""")
            .SingleAsync();
        rows.Should().Be(1, "the replaced sets are gone and the partial unique index allows one staged row");
        var staged = await harness.Reader.GetStagedAsync("ord-1", target.Id, CancellationToken.None);
        var winner = sets.Should().ContainSingle(set => set.Id == staged!.Id).Subject;
        staged.Should().BeEquivalentTo(winner, "the staged set is one writer's, whole");
    }

    [Fact]
    public async Task TheSchema_AllowsOneStagedRowPerTarget_AndAnyNumberOfDiscardedOnes()
    {
        await using var harness = await CreateHarnessAsync();
        var target = await SeedTargetAsync(harness, "ord-1", "pole-1");
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        await using var raw = PostgresTargetStoreDatabase.NewContext(connectionString);
        Task<int> InsertAsync(string status) => raw.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "ServiceOrderOccupancyChangeSets"
                ("Id", "ServiceOrderId", "TargetId", "PoleFeatureId", "ChangesJson", "BaselineVersion", "Status", "StagedBy", "StagedAt", "ClosedAt", "OrganizationId")
            VALUES ({Guid.NewGuid()}, 'ord-1', {target.Id}, 'pole-1', '[]', 0, {status}, 'crew-1', now(), NULL, {Owner})
            """);

        await InsertAsync("Discarded");
        await InsertAsync("Discarded");
        await InsertAsync("Committed");
        await InsertAsync("Staged");
        var secondStaged = () => InsertAsync("Staged");

        (await secondStaged.Should().ThrowAsync<Npgsql.PostgresException>()).Which.SqlState.Should().Be("23505");
        var definition = await raw.Database
            .SqlQuery<string>($"""SELECT indexdef AS "Value" FROM pg_indexes WHERE indexname = 'IX_ServiceOrderOccupancyChangeSets_TargetId_Staged'""")
            .SingleAsync();
        definition.Should().Contain("UNIQUE INDEX").And.Contain("""("TargetId")""").And.Contain("WHERE").And.Contain("'Staged'");
    }

    [Fact]
    public async Task StageAsync_StoresTheStagingInstantAsUtcToTheMicrosecond_AndTheChangesWithEnumNames()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedTargetAsync(harness, "ord-1", "pole-1");
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        harness.Clock.SetUtcNow(Now.AddTicks(1_234_567));
        var set = NewSet(harness, "ord-1", "pole-1", [OccupancyChangeBuilder.MarkSlotUnidentified(4)]);

        var stored = await harness.Writer.StageAsync(set, CancellationToken.None);

        stored.StagedAt.Should().Be(Now.AddTicks(1_234_560), "sub-microsecond ticks are dropped, as PostgreSQL would");
        (await harness.Reader.GetStagedAsync("ord-1", set.TargetId, CancellationToken.None)).Should().BeEquivalentTo(stored);
        await using var verify = PostgresTargetStoreDatabase.NewContext(connectionString);
        var raw = await verify.Database
            .SqlQuery<string>($"""SELECT ("Status" || '|' || "ChangesJson") AS "Value" FROM "ServiceOrderOccupancyChangeSets" """)
            .SingleAsync();
        raw.Should().Be("""Staged|[{"kind":"MarkUnidentified","slotIndex":4,"cableDiameterMm":0,"cableLoadDaN":0}]""");
    }
}
