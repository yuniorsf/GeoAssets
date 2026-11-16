using FluentAssertions;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Runs the shared target-store contract against <see cref="RestServiceOrderTargetRepository"/>
/// talking to the real <c>MapServiceOrderTargetsApi()</c> endpoints in a TestServer (XD01-308) —
/// the one runner that proves the client's and the host's error mappings agree.
///
/// The caller is an administrator of the harness tenant. Targets are arranged straight into
/// the host's store (<see cref="SeedingTargetWriter"/>), because the host has, by design, no
/// route that stores caller-built targets; reads, updates and removals go over HTTP.
/// </summary>
public sealed class RestServiceOrderTargetRepositoryContractTests : ServiceOrderTargetStoreContractTests
{
    protected override async Task<TargetStoreContractHarness> CreateHarnessAsync()
    {
        var host = await OrderTargetsApiHost.StartAsync();
        var http = host.ClientFor("admin");
        var rest = new RestServiceOrderTargetRepository(http);

        return new TargetStoreContractHarness(
            rest,
            new SeedingTargetWriter(host.Store, rest),
            host.Orders,
            host.OrderTypes,
            OrderTargetsApiHost.OrgA,
            new HarnessScope(host, http));
    }

    [Fact]
    public async Task AddRangeAsync_ThroughTheRestClient_IsNotOffered_ButItsGuardsAndTheEmptyCaseStillHold()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        using var http = host.ClientFor("admin");
        var rest = new RestServiceOrderTargetRepository(http);
        var target = new ServiceOrderTargetBuilder().WithOrderId("ord-1").WithOrganizationId(OrderTargetsApiHost.OrgA).Build();
        await host.SeedOrderAsync();

        (await rest.AddRangeAsync("ord-1", [], CancellationToken.None)).Should().Be(0);
        await FluentActions.Invoking(() => rest.AddRangeAsync("ord-1", null!, CancellationToken.None)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Invoking(() => rest.AddRangeAsync(" ", [target], CancellationToken.None)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Invoking(() => rest.AddRangeAsync("ord-1", [target], CancellationToken.None))
            .Should().ThrowAsync<NotSupportedException>().WithMessage("*ApplyAsync*");
        host.Store.Snapshot().Should().BeEmpty();
    }

    // ── Facts that cannot hold over REST, by design ───────────────────────────

    private const string NoAddRoute =
        "By design the host has no route that stores caller-built targets (a target's alias, position, conflict level and " +
        "organization are derived on the server): targets are added through IOrderTargetSelectionService.ApplyAsync, covered by " +
        "RestOrderTargetSelectionClientTests. Over REST this fact would only exercise the arrangement path into the host's store.";

    private const string HostRevalidatesEdits =
        "By design the host's PATCH route re-validates what a store takes on trust: activity ids must belong to the order type's " +
        "catalog, the status must be a move the target lifecycle allows, and the conflict level is not writable. Covered instead by " +
        "ServiceOrderTargetsEditEndpointTests and UpdateAsync_OverRest_* below.";

    [Fact(Skip = NoAddRoute)]
    public override Task AddRangeAsync_NewTargets_ReturnsHowManyWereInsertedAndStoresThemAsGiven() => Task.CompletedTask;

    [Fact(Skip = NoAddRoute)]
    public override Task AddRangeAsync_FeatureTheOrderAlreadyHolds_IsSkippedAndTheStoredTargetIsNotChanged() => Task.CompletedTask;

    [Fact(Skip = NoAddRoute)]
    public override Task AddRangeAsync_RepeatingTheSameCall_ReturnsZeroTheSecondTime() => Task.CompletedTask;

    [Fact(Skip = NoAddRoute)]
    public override Task AddRangeAsync_BatchMixingHeldAndNewFeatures_InsertsOnlyTheNewOnes() => Task.CompletedTask;

    [Fact(Skip = NoAddRoute)]
    public override Task AddRangeAsync_SameFeatureTwiceInOneCall_StoresTheFirstOnly() => Task.CompletedTask;

    [Fact(Skip = NoAddRoute)]
    public override Task AddRangeAsync_SameFeatureInTwoOrders_IsStoredForBoth() => Task.CompletedTask;

    [Fact(Skip = NoAddRoute)]
    public override Task AddRangeAsync_MoreTargetsThanOneInsertStatementCarries_InsertsThemAll() => Task.CompletedTask;

    [Fact(Skip = NoAddRoute)]
    public override Task AddRangeAsync_EmptyTargets_ReturnsZeroEvenForAnOrderThatDoesNotExist() => Task.CompletedTask;

    [Fact(Skip = NoAddRoute)]
    public override Task AddRangeAsync_BatchContainingAnotherOrdersTarget_ThrowsArgumentExceptionAndInsertsNothing() => Task.CompletedTask;

    [Fact(Skip = NoAddRoute)]
    public override Task AddRangeAsync_OrderThatDoesNotExist_ThrowsKeyNotFoundExceptionAndInsertsNothing() => Task.CompletedTask;

    [Fact(Skip = NoAddRoute)]
    public override Task AddRangeAsync_TargetCarryingAnotherOrganizationThanItsOrder_ThrowsArgumentExceptionAndInsertsNothing() => Task.CompletedTask;

    [Fact(Skip = NoAddRoute)]
    public override Task AddRangeAsync_NullTargets_ThrowsArgumentNullException() => Task.CompletedTask;

    [Theory(Skip = NoAddRoute)]
    [InlineData("")]
    public override Task AddRangeAsync_BlankOrderId_ThrowsArgumentException(string orderId) => Task.FromResult(orderId);

    [Fact(Skip = HostRevalidatesEdits)]
    public override Task UpdateAsync_WritesActivitiesStatusConflictLevelAndNote_AndNothingElse() => Task.CompletedTask;

    [Fact(Skip = HostRevalidatesEdits)]
    public override Task UpdateAsync_DoesNotRecheckStatusMoves() => Task.CompletedTask;

    // ── What holds over REST instead ──────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_OverRest_WritesCatalogActivitiesAnAllowedStatusMoveAndTheNote_ButNeverTheConflictLevel()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        using var http = host.ClientFor("admin");
        var rest = new RestServiceOrderTargetRepository(http);
        await host.SeedOrderAsync();
        var stored = (await host.SeedTargetsAsync("ord-1", 1))[0];

        await rest.UpdateAsync(
            stored with
            {
                ActivityIds = ["replace", "inspect"],
                Status = OrderTargetStatus.InProgress,
                Note = "edited",
                ConflictLevel = OrderTargetConflictLevel.Blocked,
                Alias = "ignored",
            },
            CancellationToken.None);

        var after = await rest.GetByIdAsync("ord-1", stored.Id, CancellationToken.None);
        after.Should().BeEquivalentTo(stored with
        {
            ActivityIds = ["replace", "inspect"],
            Status = OrderTargetStatus.InProgress,
            Note = "edited",
            Version = 1,
        });
    }

    [Fact]
    public async Task UpdateAsync_OverRest_ActivityOutsideTheCatalogOrDisallowedStatusMove_ThrowsArgumentException_AndWritesNothing()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        using var http = host.ClientFor("admin");
        var rest = new RestServiceOrderTargetRepository(http);
        await host.SeedOrderAsync();
        var stored = (await host.SeedTargetsAsync("ord-1", 1))[0];

        var unknownActivity = () => rest.UpdateAsync(stored with { ActivityIds = ["paint"] }, CancellationToken.None);
        var illegalMove = () => rest.UpdateAsync(stored with { Status = OrderTargetStatus.Done }, CancellationToken.None);

        await unknownActivity.Should().ThrowAsync<ArgumentException>();
        await illegalMove.Should().ThrowAsync<ArgumentException>();
        host.Store.Snapshot().Single().Should().BeEquivalentTo(stored);
    }

    [Fact]
    public async Task EditsOverRest_ByACallerTheOrderRulesRefuse_ThrowOrderActionDeniedException()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        using var http = host.ClientFor("reader");
        var rest = new RestServiceOrderTargetRepository(http);
        await host.SeedOrderAsync();
        var stored = (await host.SeedTargetsAsync("ord-1", 1))[0];

        var update = () => rest.UpdateAsync(stored with { Note = "x" }, CancellationToken.None);
        var remove = () => rest.RemoveAsync("ord-1", [stored.Id], CancellationToken.None);

        (await update.Should().ThrowAsync<GeoAssets.Workflow.Rules.OrderActionDeniedException>())
            .Which.Action.Should().Be(GeoAssets.Workflow.Orders.OrderActionType.Annotate);
        await remove.Should().ThrowAsync<GeoAssets.Workflow.Rules.OrderActionDeniedException>();
        host.Store.Snapshot().Should().ContainSingle();
    }

    [Fact]
    public async Task FindOpenUsagesAsync_OverRest_MoreIdsThanOneRequestCarries_AreSentInChunksAndEveryPageIsDrained()
    {
        await using var host = await OrderTargetsApiHost.StartAsync();
        using var http = host.ClientFor("admin");
        var rest = new RestServiceOrderTargetRepository(http);
        await host.SeedOrderAsync();
        await host.SeedOrderAsync("ord-holder", status: GeoAssets.Workflow.Orders.ServiceOrderStatus.InProgress);
        await host.SeedTargetsAsync("ord-holder", 700);
        var wanted = Enumerable.Range(1, 1201).Select(i => $"f-{i:000}").ToArray();

        var usages = await rest.FindOpenUsagesAsync(wanted, "ord-1", CancellationToken.None);

        usages.Should().HaveCount(700).And.OnlyHaveUniqueItems(u => u.FeatureId);
    }

    /// <summary>Arranges through the host's store; edits and removes through the REST client under test.</summary>
    private sealed class SeedingTargetWriter(IServiceOrderTargetWriter store, IServiceOrderTargetWriter rest) : IServiceOrderTargetWriter
    {
        public Task<int> AddRangeAsync(string orderId, IReadOnlyCollection<ServiceOrderTarget> targets, CancellationToken ct) =>
            store.AddRangeAsync(orderId, targets, ct);

        public Task UpdateAsync(ServiceOrderTarget target, CancellationToken ct) => rest.UpdateAsync(target, ct);

        public Task<int> RemoveAsync(string orderId, IReadOnlyCollection<string> targetIds, CancellationToken ct) =>
            rest.RemoveAsync(orderId, targetIds, ct);
    }

    private sealed class HarnessScope(OrderTargetsApiHost host, HttpClient http) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            http.Dispose();
            return host.DisposeAsync();
        }
    }
}
