using FluentAssertions;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

/// <summary>XD01-362: <see cref="ITransitionGuard"/>s run by <see cref="ValidatingServiceOrderRepository"/> on an actual status change.</summary>
public class ValidatingServiceOrderRepositoryGuardTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 18, 10, 0, 0, TimeSpan.Zero);

    private sealed class RecordingGuard(Func<OrderTransitionContext, GuardVerdict> verdict, List<string>? calls = null, string name = "guard") : ITransitionGuard
    {
        public List<OrderTransitionContext> Contexts { get; } = [];

        public Task<GuardVerdict> EvaluateAsync(OrderTransitionContext context, CancellationToken cancellationToken)
        {
            Contexts.Add(context);
            calls?.Add(name);
            return Task.FromResult(verdict(context));
        }
    }

    private static RecordingGuard Allowing(List<string>? calls = null, string name = "guard") => new(_ => GuardVerdict.Allow, calls, name);

    private static RecordingGuard Denying(string code = "test.denied", List<string>? calls = null, string name = "guard") =>
        new(_ => GuardVerdict.Deny(code, $"{code} reason"), calls, name);

    private static async Task<(FakeServiceOrderRepository Inner, ValidatingServiceOrderRepository Sut)> NewAsync(
        OrderTypeRegistry? registry = null, params ITransitionGuard[] guards)
    {
        var inner = new FakeServiceOrderRepository();
        await inner.AddAsync(new ServiceOrder { Id = "ord-1", Title = "before", OrderTypeId = "t", Status = ServiceOrderStatus.Draft });
        return (inner, new ValidatingServiceOrderRepository(inner, registry, guards, new FakeTimeProvider(Now)));
    }

    private static ServiceOrder Moved(string status, string title = "after") =>
        new() { Id = "ord-1", Title = title, OrderTypeId = "t", Status = status };

    [Fact]
    public async Task UpdateAsync_GuardDenies_ThrowsTypedDenial_AndPersistsNothing()
    {
        var (inner, sut) = await NewAsync(guards: Denying("test.nope"));

        var act = () => sut.UpdateAsync(Moved(ServiceOrderStatus.Pending));

        var thrown = (await act.Should().ThrowAsync<TransitionGuardDeniedException>()).Which;
        thrown.Verdict.Code.Should().Be("test.nope");
        thrown.From.Should().Be(ServiceOrderStatus.Draft);
        thrown.To.Should().Be(ServiceOrderStatus.Pending);
        var stored = (await inner.GetByIdAsync("ord-1"))!;
        stored.Status.Should().Be(ServiceOrderStatus.Draft);
        stored.Title.Should().Be("before");
    }

    [Fact]
    public async Task AppendActionAsync_GuardDenies_ThrowsTypedDenial_AndPersistsNothing()
    {
        var (inner, sut) = await NewAsync(guards: Denying("test.nope"));
        var entry = new OrderActionLog(OrderActionType.Cancel, "u1", Now.UtcDateTime, ResultingStatus: ServiceOrderStatus.Cancelled);

        var act = () => sut.AppendActionAsync("ord-1", entry);

        var thrown = (await act.Should().ThrowAsync<TransitionGuardDeniedException>()).Which;
        thrown.Verdict.Code.Should().Be("test.nope");
        thrown.To.Should().Be(ServiceOrderStatus.Cancelled);
        var stored = (ServiceOrder)(await inner.GetByIdAsync("ord-1"))!;
        stored.Status.Should().Be(ServiceOrderStatus.Draft);
        stored.ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task GuardsRunInOrder_FirstDenialWins()
    {
        var calls = new List<string>();
        var (_, sut) = await NewAsync(
            guards: [Allowing(calls, "a"), Denying("test.first", calls, "b"), Denying("test.second", calls, "c")]);

        var act = () => sut.UpdateAsync(Moved(ServiceOrderStatus.Pending));

        (await act.Should().ThrowAsync<TransitionGuardDeniedException>()).Which.Verdict.Code.Should().Be("test.first");
        calls.Should().Equal("a", "b");
    }

    [Fact]
    public async Task NoStatusChange_GuardsAreNotConsulted()
    {
        var guard = Denying();
        var (inner, sut) = await NewAsync(guards: guard);

        await sut.UpdateAsync(Moved(ServiceOrderStatus.Draft, title: "renamed"));
        await sut.AppendActionAsync("ord-1", new OrderActionLog(OrderActionType.Annotate, "u1", Now.UtcDateTime, "note"));

        guard.Contexts.Should().BeEmpty();
        (await inner.GetByIdAsync("ord-1"))!.Title.Should().Be("renamed");
    }

    [Fact]
    public async Task AllGuardsAllow_TransitionIsPersisted()
    {
        var (inner, sut) = await NewAsync(guards: [Allowing(), Allowing()]);

        await sut.UpdateAsync(Moved(ServiceOrderStatus.Pending));

        (await inner.GetByIdAsync("ord-1"))!.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    [Fact]
    public async Task GuardContext_CarriesFromToOrderTypeAndClock()
    {
        var registry = new OrderTypeRegistry();
        var type = new OrderType { Id = "t", DisplayName = "t" };
        registry.Register(type);
        var guard = Allowing();
        var (_, sut) = await NewAsync(registry, guard);

        await sut.UpdateAsync(Moved(ServiceOrderStatus.Pending));

        var context = guard.Contexts.Should().ContainSingle().Subject;
        context.FromStatus.Should().Be(ServiceOrderStatus.Draft);
        context.ToStatus.Should().Be(ServiceOrderStatus.Pending);
        context.OrderType.Should().BeSameAs(type);
        context.Order.Id.Should().Be("ord-1");
        context.OccurredAt.Should().Be(Now);
    }
}
