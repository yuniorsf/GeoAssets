using FluentAssertions;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// <see cref="EFOccupancyApplySagaStore"/> (XD01-383) over PostgreSQL 16 (Testcontainers): the saga's writes are guarded by
/// state — a saga begins once, ends once, and a set that is no longer staged refuses it — its reads are scoped to the
/// organization, and the timeout monitor sees every organization's pending sagas at once.
/// </summary>
public sealed class PostgresOccupancyApplySagaStoreTests : IAsyncDisposable
{
    private const string DatabaseName = "occupancy_apply_saga_store";

    private static readonly Guid Owner = ServiceOrderTargetBuilder.DefaultOrganizationId;
    private static readonly Guid Other = new("0b0b0b0b-0000-4000-8000-000000000383");
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);

    private readonly DisposableGroup _scope = new();
    private string _connectionString = string.Empty;

    private async Task FreshAsync()
    {
        _connectionString = await PostgresTargetStoreDatabase.EnsureCreatedAsync(DatabaseName);
        await using var db = PostgresTargetStoreDatabase.NewContext(_connectionString);
        await db.Database.ExecuteSqlRawAsync(
            """
            TRUNCATE TABLE "ServiceOrders", neutral_operator.field_pole_occupancies, neutral_operator.occupancy_apply_sagas, neutral_operator.occupancy_apply_saga_poles CASCADE;
            """);
    }

    public ValueTask DisposeAsync() => _scope.DisposeAsync();

    private (EFOccupancyApplySagaStore Store, EFPoleOccupancyRepository Poles, EFOccupancyChangeSetRepository Sets, ServiceOrderDbContext Kernel) For(Guid organization, FakeTimeProvider? clock = null)
    {
        var kernel = _scope.Add(PostgresTargetStoreDatabase.NewContext(_connectionString, new FakeTenantContext(organization)));
        var timeProvider = clock ?? new FakeTimeProvider(Now);
        var poles = new EFPoleOccupancyRepository(NeutralOperatorTestContext.Over(kernel), timeProvider);
        var sets = new EFOccupancyChangeSetRepository(NeutralOperatorTestContext.Over(kernel), timeProvider);
        return (new EFOccupancyApplySagaStore(NeutralOperatorTestContext.Over(kernel), poles), poles, sets, kernel);
    }

    private static OccupancyApplySaga NewSaga(string orderId, Guid organization, int attempt = 1, DateTimeOffset? lastRequestedAt = null)
    {
        var batch = AssetMutationBatchRequestedEvent.Create(
            "catalog", organization, "tests", "approver-1", [], new FakeTimeProvider(Now), batchId: OccupancyCommitService.AssetMutationBatchId(orderId, attempt),
            sourceOrderId: orderId, sourceVerticalId: "neutral-operator");
        return new OccupancyApplySaga
        {
            BatchId = batch.BatchId,
            Attempt = attempt,
            OrderId = orderId,
            OrderTypeId = "shared-use-pole-work",
            OrganizationId = organization,
            PreviousStatus = "Completed",
            ApprovedStatus = "Approved",
            RequestedBy = "approver-1",
            RequestedAt = lastRequestedAt ?? Now,
            LastRequestedAt = lastRequestedAt ?? Now,
            State = OccupancyApplySagaState.Pending,
            Batch = batch,
        };
    }

    [Fact]
    public async Task BeginAsync_ThenFind_ReadsTheSagaWithItsBatchAndItsPolesBack()
    {
        await FreshAsync();
        var (store, _, _, _) = For(Owner);
        var saga = NewSaga("ord-begin", Owner);
        var pole = new OccupancyApplySagaPole("pole-1", 0, 1, []);

        (await store.BeginAsync(saga, [pole], [], CancellationToken.None)).Should().BeTrue();

        var found = (await store.FindAsync(saga.BatchId, CancellationToken.None))!;
        found.Should().BeEquivalentTo(saga);
        found.Batch.SourceVerticalId.Should().Be("neutral-operator");
        (await store.GetPolesAsync(saga.BatchId, CancellationToken.None)).Should().BeEquivalentTo([pole]);
        (await store.FindLatestForOrderAsync("ord-begin", CancellationToken.None))!.BatchId.Should().Be(saga.BatchId);
    }

    [Fact]
    public async Task BeginAsync_ForASagaWhoseSetsAreNoLongerStaged_RefusesAndWritesNothing()
    {
        await FreshAsync();
        var (store, _, _, _) = For(Owner);
        var saga = NewSaga("ord-gone", Owner);

        var began = await store.BeginAsync(saga, [new OccupancyApplySagaPole("pole-1", 0, 1, [])], [Guid.NewGuid()], CancellationToken.None);

        began.Should().BeFalse("the set it names is not on file as staged");
        (await store.FindAsync(saga.BatchId, CancellationToken.None)).Should().BeNull();
        (await store.GetPolesAsync(saga.BatchId, CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task BeginAsync_ForAnotherOrganizationsSaga_ThrowsArgumentException()
    {
        await FreshAsync();
        var (store, _, _, _) = For(Owner);

        var act = () => store.BeginAsync(NewSaga("ord-x", Other), [], [], CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task BeginAsync_ForASagaThatIsNotPending_ThrowsArgumentException()
    {
        await FreshAsync();
        var (store, _, _, _) = For(Owner);

        var act = () => store.BeginAsync(NewSaga("ord-x", Owner) with { State = OccupancyApplySagaState.Committed, CompletedAt = Now }, [], [], CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CompleteAsync_EndsAPendingSagaOnce_AndASecondCompletionIsRefused()
    {
        await FreshAsync();
        var (store, _, _, _) = For(Owner);
        var saga = NewSaga("ord-complete", Owner);
        await store.BeginAsync(saga, [], [], CancellationToken.None);
        var committed = saga with { State = OccupancyApplySagaState.Committed, CompletedAt = Now.AddMinutes(1) };

        var first = await store.CompleteAsync(committed, CancellationToken.None);
        var second = await store.CompleteAsync(saga with { State = OccupancyApplySagaState.Reverted, CompletedAt = Now.AddMinutes(2), ReasonCode = "x", Reason = "y" }, CancellationToken.None);

        first.Completed.Should().BeTrue();
        second.Completed.Should().BeFalse("a saga that is no longer pending is never changed again");
        var stored = (await store.FindAsync(saga.BatchId, CancellationToken.None))!;
        stored.State.Should().Be(OccupancyApplySagaState.Committed);
        stored.CompletedAt.Should().Be(Now.AddMinutes(1));
    }

    [Fact]
    public async Task CompleteAsync_ForASagaThatIsStillPending_OrHasNoInstant_ThrowsArgumentException()
    {
        await FreshAsync();
        var (store, _, _, _) = For(Owner);
        var saga = NewSaga("ord-bad", Owner);

        var pending = () => store.CompleteAsync(saga, CancellationToken.None);
        var noInstant = () => store.CompleteAsync(saga with { State = OccupancyApplySagaState.Committed }, CancellationToken.None);

        await pending.Should().ThrowAsync<ArgumentException>();
        await noInstant.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CompleteAsync_RevertingWhileAPoleMovedSinceTheApproval_LeavesThePole_AndNamesItUnrestored()
    {
        await FreshAsync();
        var (store, poles, _, _) = For(Owner);
        var written = await poles.ReplaceEntriesAsync("pole-moved", PoleOccupancy.UnsavedVersion, [], CancellationToken.None);
        var saga = NewSaga("ord-moved", Owner);
        await store.BeginAsync(saga, [new OccupancyApplySagaPole("pole-moved", 0, written.Version, [])], [], CancellationToken.None);
        // A data correction writes the pole after the approval: the compensation no longer knows what to restore over.
        await poles.ReplaceEntriesAsync("pole-moved", written.Version, [], CancellationToken.None);

        var completion = await store.CompleteAsync(saga with { State = OccupancyApplySagaState.Reverted, CompletedAt = Now.AddMinutes(1), ReasonCode = "x", Reason = "y" }, CancellationToken.None);

        completion.Completed.Should().BeTrue();
        completion.UnrestoredFeatureIds.Should().Equal("pole-moved");
        (await poles.GetForPoleAsync("pole-moved", CancellationToken.None))!.Version.Should().Be(written.Version + 1, "the later write wins");
        var stored = (await store.FindAsync(saga.BatchId, CancellationToken.None))!;
        stored.State.Should().Be(OccupancyApplySagaState.Reverted);
        stored.UnrestoredFeatureIds.Should().Equal("pole-moved");
    }

    [Fact]
    public async Task FindLatestForOrderAsync_AnswersTheHighestAttempt_AndFindPendingPolesOnlyThoseOfPendingSagas()
    {
        await FreshAsync();
        var (store, _, _, _) = For(Owner);
        var first = NewSaga("ord-latest", Owner, attempt: 1);
        var second = NewSaga("ord-latest", Owner, attempt: 2);
        await store.BeginAsync(first, [new OccupancyApplySagaPole("pole-a", 0, 1, []), new OccupancyApplySagaPole("pole-b", 0, 1, [])], [], CancellationToken.None);
        await store.CompleteAsync(first with { State = OccupancyApplySagaState.Committed, CompletedAt = Now }, CancellationToken.None);
        await store.BeginAsync(second, [new OccupancyApplySagaPole("pole-b", 3, 4, []), new OccupancyApplySagaPole("pole-c", 0, 1, [])], [], CancellationToken.None);

        (await store.FindLatestForOrderAsync("ord-latest", CancellationToken.None))!.Attempt.Should().Be(2);
        (await store.FindLatestForOrderAsync("ord-none", CancellationToken.None)).Should().BeNull();
        (await store.FindPendingPolesAsync(["pole-a", "pole-b", "pole-c", "pole-d"], CancellationToken.None)).Should().Equal("pole-b", "pole-c");
        (await store.FindPendingPolesAsync([], CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task Reads_AreScopedToTheCallersOrganization_AnotherOrganizationsSagaIsNotFound()
    {
        await FreshAsync();
        var (ownerStore, _, _, _) = For(Owner);
        var (otherStore, _, _, _) = For(Other);
        var saga = NewSaga("ord-private", Owner);
        await ownerStore.BeginAsync(saga, [new OccupancyApplySagaPole("pole-private", 0, 1, [])], [], CancellationToken.None);

        (await otherStore.FindAsync(saga.BatchId, CancellationToken.None)).Should().BeNull();
        (await otherStore.FindLatestForOrderAsync("ord-private", CancellationToken.None)).Should().BeNull();
        (await otherStore.FindPendingPolesAsync(["pole-private"], CancellationToken.None)).Should().BeEmpty();
        (await ownerStore.FindAsync(saga.BatchId, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task RecordRetryAsync_CountsTheRetry_RestartsTheTimeout_AndClearsTheTimedOutFlag_OfAPendingSagaOnly()
    {
        await FreshAsync();
        var (store, _, _, _) = For(Owner);
        var saga = NewSaga("ord-retry", Owner, lastRequestedAt: Now.AddHours(-1));
        await store.BeginAsync(saga, [], [], CancellationToken.None);
        await store.MarkTimedOutAsync(Now, Now, 10, CancellationToken.None);
        (await store.FindAsync(saga.BatchId, CancellationToken.None))!.TimedOutAt.Should().NotBeNull();

        var retried = await store.RecordRetryAsync(saga.BatchId, Now.AddMinutes(5), CancellationToken.None);

        retried.Should().BeTrue();
        var stored = (await store.FindAsync(saga.BatchId, CancellationToken.None))!;
        stored.RetryCount.Should().Be(1);
        stored.LastRequestedAt.Should().Be(Now.AddMinutes(5));
        stored.TimedOutAt.Should().BeNull();
        await store.CompleteAsync(stored with { State = OccupancyApplySagaState.Committed, CompletedAt = Now.AddMinutes(6) }, CancellationToken.None);
        (await store.RecordRetryAsync(saga.BatchId, Now.AddMinutes(7), CancellationToken.None)).Should().BeFalse("an ended saga is not retried");
        (await store.RecordRetryAsync(Guid.NewGuid(), Now, CancellationToken.None)).Should().BeFalse();
    }

    // ── The timeout monitor: every organization at once ───────────────────────

    [Fact]
    public async Task MarkTimedOutAsync_FlagsEveryOrganizationsStalePendingSagas_OldestFirst_UpToTheLimit_AndEachOnlyOnce()
    {
        await FreshAsync();
        var (ownerStore, _, _, _) = For(Owner);
        var (otherStore, _, _, _) = For(Other);
        var monitor = ownerStore;
        var oldest = NewSaga("ord-oldest", Other, lastRequestedAt: Now.AddHours(-5));
        var middle = NewSaga("ord-middle", Owner, lastRequestedAt: Now.AddHours(-3));
        var newest = NewSaga("ord-newest", Owner, lastRequestedAt: Now.AddHours(-2));
        var fresh = NewSaga("ord-fresh", Other, lastRequestedAt: Now.AddMinutes(-5));
        var done = NewSaga("ord-done", Owner, lastRequestedAt: Now.AddHours(-9));
        await otherStore.BeginAsync(oldest, [], [], CancellationToken.None);
        await ownerStore.BeginAsync(middle, [], [], CancellationToken.None);
        await ownerStore.BeginAsync(newest, [], [], CancellationToken.None);
        await otherStore.BeginAsync(fresh, [], [], CancellationToken.None);
        await ownerStore.BeginAsync(done, [], [], CancellationToken.None);
        await ownerStore.CompleteAsync(done with { State = OccupancyApplySagaState.Committed, CompletedAt = Now }, CancellationToken.None);

        var firstPage = await monitor.MarkTimedOutAsync(Now.AddMinutes(-15), Now, 2, CancellationToken.None);
        var secondPage = await monitor.MarkTimedOutAsync(Now.AddMinutes(-15), Now, 2, CancellationToken.None);
        var thirdPage = await monitor.MarkTimedOutAsync(Now.AddMinutes(-15), Now, 2, CancellationToken.None);

        firstPage.Select(s => s.OrderId).Should().Equal("ord-oldest", "ord-middle");
        firstPage.Should().OnlyContain(s => s.TimedOutAt == Now);
        secondPage.Select(s => s.OrderId).Should().Equal("ord-newest");
        thirdPage.Should().BeEmpty("a flagged saga is not flagged again");
        (await otherStore.FindAsync(oldest.BatchId, CancellationToken.None))!.State.Should().Be(OccupancyApplySagaState.Pending, "a timeout never reverts");
        (await otherStore.FindAsync(fresh.BatchId, CancellationToken.None))!.TimedOutAt.Should().BeNull();
        (await ownerStore.FindAsync(done.BatchId, CancellationToken.None))!.TimedOutAt.Should().BeNull("an ended saga does not time out");
    }

    [Fact]
    public async Task MarkTimedOutAsync_WithANonPositiveLimit_ThrowsArgumentOutOfRangeException()
    {
        await FreshAsync();
        var (store, _, _, _) = For(Owner);

        var act = () => store.MarkTimedOutAsync(Now, Now, 0, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetBacklogAsync_CountsEveryOrganizationsPendingSagas_AndNamesTheOldestRequest()
    {
        await FreshAsync();
        var (ownerStore, _, _, _) = For(Owner);
        var (otherStore, _, _, _) = For(Other);
        (await ownerStore.GetBacklogAsync(CancellationToken.None)).Should().Be((0, null));
        await ownerStore.BeginAsync(NewSaga("ord-a", Owner, lastRequestedAt: Now.AddMinutes(-10)), [], [], CancellationToken.None);
        await otherStore.BeginAsync(NewSaga("ord-b", Other, lastRequestedAt: Now.AddMinutes(-30)), [], [], CancellationToken.None);
        var ended = NewSaga("ord-c", Owner, lastRequestedAt: Now.AddMinutes(-90));
        await ownerStore.BeginAsync(ended, [], [], CancellationToken.None);
        await ownerStore.CompleteAsync(ended with { State = OccupancyApplySagaState.Committed, CompletedAt = Now }, CancellationToken.None);

        var (pending, oldest) = await ownerStore.GetBacklogAsync(CancellationToken.None);

        pending.Should().Be(2, "the monitor sees both organizations, not the ended saga");
        oldest.Should().Be(Now.AddMinutes(-30));
    }

    [Fact]
    public async Task TheMonitor_HonorsACancelledToken()
    {
        await FreshAsync();
        var (store, _, _, _) = For(Owner);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var mark = () => store.MarkTimedOutAsync(Now, Now, 1, cts.Token);
        var backlog = () => store.GetBacklogAsync(cts.Token);

        await mark.Should().ThrowAsync<OperationCanceledException>();
        await backlog.Should().ThrowAsync<OperationCanceledException>();
    }
}
