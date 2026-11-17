using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// <see cref="EFWorkflowUnitOfWork"/> (XD01-316): the work of one unit is committed together or
/// not at all, a unit started inside a transaction joins it, a rolled-back unit leaves nothing
/// tracked, and EF's lost-write signal comes out as the workflow's own exception.
/// </summary>
public sealed class EFWorkflowUnitOfWorkTests
{
    private static ServiceOrder Order(string id, string title = "Order") => new()
    {
        Id = id,
        Title = title,
        OrderTypeId = "inspection",
        CreatedBy = "tester",
        CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public async Task ExecuteAsync_CommitsEverythingTheWorkWrote_AndReturnsItsResult()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        var sut = new EFWorkflowUnitOfWork(fixture.Context);

        var result = await sut.ExecuteAsync(
            async ct =>
            {
                await repo.AddAsync(Order("a"), ct);
                await repo.AddAsync(Order("b"), ct);
                return 2;
            },
            CancellationToken.None);

        result.Should().Be(2);
        fixture.Context.Database.CurrentTransaction.Should().BeNull("the unit's own transaction is finished");
        await using var other = fixture.NewContext();
        (await other.ServiceOrders.Select(o => o.Id).OrderBy(id => id).ToListAsync()).Should().Equal("a", "b");
    }

    [Fact]
    public async Task ExecuteAsync_WhenTheWorkThrows_RollsBackWhatItWrote_ClearsTheChangeTracker_AndRethrows()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        var sut = new EFWorkflowUnitOfWork(fixture.Context);

        var act = () => sut.ExecuteAsync<int>(
            async ct =>
            {
                await repo.AddAsync(Order("a"), ct);
                fixture.Context.ServiceOrders.Add(ServiceOrderMapper.ToRecord(Order("pending-insert")));
                throw new InvalidOperationException("second step failed");
            },
            CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("second step failed");
        fixture.Context.Database.CurrentTransaction.Should().BeNull();
        fixture.Context.ChangeTracker.Entries().Should().BeEmpty();
        await repo.AddAsync(Order("later"));
        await using var other = fixture.NewContext();
        (await other.ServiceOrders.Select(o => o.Id).ToListAsync())
            .Should().Equal(["later"], "neither the rolled-back row nor the one left pending was written by the later save");
    }

    [Fact]
    public async Task ExecuteAsync_InsideAnExistingTransaction_JoinsIt_AndLeavesCommitAndRollbackToItsOwner()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        var sut = new EFWorkflowUnitOfWork(fixture.Context);

        await using (var outer = await fixture.Context.Database.BeginTransactionAsync())
        {
            await sut.ExecuteAsync(async ct => { await repo.AddAsync(Order("a"), ct); return 0; }, CancellationToken.None);

            fixture.Context.Database.CurrentTransaction.Should().BeSameAs(outer);
            await outer.RollbackAsync();
        }

        fixture.Context.ChangeTracker.Clear();
        (await fixture.Context.ServiceOrders.CountAsync()).Should().Be(0, "the owner of the transaction rolled the joined unit back");
    }

    [Fact]
    public async Task ExecuteAsync_InsideAnExistingTransaction_AFailureIsRethrown_WithoutRollingTheOuterTransactionBack()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        var sut = new EFWorkflowUnitOfWork(fixture.Context);
        await using var outer = await fixture.Context.Database.BeginTransactionAsync();
        await repo.AddAsync(Order("before"));

        var act = () => sut.ExecuteAsync<int>(_ => throw new InvalidOperationException("inner failed"), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        fixture.Context.Database.CurrentTransaction.Should().BeSameAs(outer, "whether to roll back is the outer unit's decision");
        await outer.CommitAsync();
        await using var other = fixture.NewContext();
        (await other.ServiceOrders.Select(o => o.Id).ToListAsync()).Should().Equal("before");
    }

    [Fact]
    public async Task ExecuteAsync_AWriteLostToAnotherWriter_SurfacesAsServiceOrderConcurrencyException_NamingTheOrder()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("a"));
        var sut = new EFWorkflowUnitOfWork(fixture.Context);
        await using (var other = fixture.NewContext())
        {
            // Another writer changes the order after this context read it.
            var theirs = await other.ServiceOrders.SingleAsync(o => o.Id == "a");
            theirs.Title = "Theirs";
            await other.SaveChangesAsync();
        }

        var act = () => sut.ExecuteAsync(
            async ct =>
            {
                // AppendActionAsync is the writer that lets EF's own exception through.
                await repo.AppendActionAsync("a", new OrderActionLog(OrderActionType.Annotate, "tester", new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc)), ct);
                return 0;
            },
            CancellationToken.None);

        (await act.Should().ThrowAsync<ServiceOrderConcurrencyException>()).Which.OrderId.Should().Be("a");
        await using var verify = fixture.NewContext();
        (await verify.OrderActionLogs.CountAsync()).Should().Be(0, "the log entry went with the lost write");
        (await verify.ServiceOrders.SingleAsync(o => o.Id == "a")).Title.Should().Be("Theirs");
    }

    [Fact]
    public async Task ExecuteAsync_TokenAlreadyCancelled_ThrowsBeforeTheWorkRuns_AndOpensNoTransaction()
    {
        using var fixture = new SqliteFixture();
        var sut = new EFWorkflowUnitOfWork(fixture.Context);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var ran = false;

        var act = () => sut.ExecuteAsync(_ => { ran = true; return Task.FromResult(0); }, cancelled.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        ran.Should().BeFalse();
        fixture.Context.Database.CurrentTransaction.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_CancelledWhileTheWorkRuns_RollsBack()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        var sut = new EFWorkflowUnitOfWork(fixture.Context);
        using var cancellation = new CancellationTokenSource();

        var act = () => sut.ExecuteAsync(
            async ct =>
            {
                await repo.AddAsync(Order("a"), ct);
                await cancellation.CancelAsync();
                ct.ThrowIfCancellationRequested();
                return 0;
            },
            cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        await using var other = fixture.NewContext();
        (await other.ServiceOrders.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_NullWork_ThrowsArgumentNullException()
    {
        using var fixture = new SqliteFixture();
        var sut = new EFWorkflowUnitOfWork(fixture.Context);

        var act = () => sut.ExecuteAsync<int>(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
