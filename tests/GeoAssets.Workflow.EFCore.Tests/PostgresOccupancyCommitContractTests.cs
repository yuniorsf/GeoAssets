using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// Runs the shared <see cref="OccupancyCommitContractTests"/> against
/// <see cref="OccupancyCommitService"/> on the EF Core stores over PostgreSQL 16 (Testcontainers)
/// (XD01-318) — and adds what needs real connections: several approvals of one order at once.
/// </summary>
public sealed class PostgresOccupancyCommitContractTests : OccupancyCommitContractTests
{
    private const string DatabaseName = "occupancy_commit_contract";

    private static readonly Guid Owner = ServiceOrderTargetBuilder.DefaultOrganizationId;

    protected override async Task<OccupancyCommitContractHarness> CreateHarnessAsync()
    {
        var connectionString = await FreshDatabaseAsync();
        var scope = new DisposableGroup();
        var context = scope.Add(PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Owner)));
        return new OccupancyCommitEfHarness(context, Owner).ToContractHarness(scope);
    }

    private static async Task<string> FreshDatabaseAsync()
    {
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        await using var db = PostgresTargetStoreDatabase.NewContext(connectionString);
        await db.Database.ExecuteSqlRawAsync(
            """TRUNCATE TABLE "ServiceOrders", "WorkflowOutboxMessages", field_pole_occupancies CASCADE;""");
        return connectionString;
    }

    [Fact]
    public async Task ApproveAndCommitAsync_ManyConnectionsApprovingTheSameOrderAtOnce_CommitItExactlyOnce()
    {
        const int Callers = 6;
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Completed, "pole-1", "pole-2");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        await StageAsync(harness, "ord-1", "pole-1", OccupancyChangeBuilder.Add(2));
        await StageAsync(harness, "ord-1", "pole-2", OccupancyChangeBuilder.Add(1), OccupancyChangeBuilder.AddBeyondRegulatedSlots());
        var connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var calls = Enumerable.Range(0, Callers).Select(async _ =>
        {
            await using var context = PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Owner));
            await context.Database.OpenConnectionAsync();
            var caller = new OccupancyCommitEfHarness(context, Owner).ToContractHarness();
            SharedUseOrderTypesRegistered(caller);
            await start.Task;
            try
            {
                await ApproveAsync(caller, "ord-1");
                return (Exception?)null;
            }
            catch (Exception ex) when (ex is OccupancyConflictException or ServiceOrderConcurrencyException or InvalidServiceOrderTransitionException)
            {
                return ex;
            }
        }).ToList();
        start.SetResult();
        var outcomes = await Task.WhenAll(calls);

        outcomes.Count(outcome => outcome is null).Should().Be(1, "one approval commits; every other is refused, none fails otherwise");
        await using var verify = PostgresTargetStoreDatabase.NewContext(connectionString, new FakeTenantContext(Owner));
        var check = new OccupancyCommitEfHarness(verify, Owner).ToContractHarness();
        (await check.OccupancyReader.GetForPoleAsync("pole-1", CancellationToken.None))!.Version.Should().Be(2, "pole-1 was written once");
        (await check.OccupancyReader.GetForPoleAsync("pole-2", CancellationToken.None))!.Entries.Should().HaveCount(2);
        (await check.StagedBatchesAsync(CancellationToken.None)).Should().ContainSingle();
        (await verify.OrderActionLogs.CountAsync(a => a.ServiceOrderId == "ord-1")).Should().Be(1);
        (await SetsAsync(check, "ord-1")).Should().OnlyContain(set => set.Status == OccupancyChangeSetStatus.Committed);
    }

    private static void SharedUseOrderTypesRegistered(OccupancyCommitContractHarness harness) =>
        GeoAssets.Workflow.Isp.NeutralOperator.SharedUse.SharedUseOrderTypes.RegisterAll(harness.OrderTypes, [PoleType]);
}
