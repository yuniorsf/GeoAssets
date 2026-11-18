using FluentAssertions;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

public sealed class OrderActionHandlerRegistryTests
{
    private static readonly OrderType Inspection = new() { Id = "inspection", DisplayName = "Inspection" };
    private static readonly OrderType Maintenance = new() { Id = "maintenance", DisplayName = "Maintenance" };

    private static OrderActionLog Entry(OrderActionType action) => new(action, "user", DateTime.UnixEpoch);

    private abstract class ClaimingHandler(Func<OrderType?, OrderActionType, bool> claims) : IOrderActionHandler
    {
        public int Probes { get; private set; }

        public bool CanHandle(OrderType? orderType, OrderActionLog entry)
        {
            Probes++;
            return claims(orderType, entry.Action);
        }

        public Task<OrderActionOutcome> HandleAsync(OrderActionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(OrderActionOutcome.Applied);
    }

    private sealed class FirstHandler(Func<OrderType?, OrderActionType, bool> claims) : ClaimingHandler(claims);

    private sealed class SecondHandler(Func<OrderType?, OrderActionType, bool> claims) : ClaimingHandler(claims);

    [Fact]
    public void Find_NoHandlers_ReturnsNull()
    {
        new OrderActionHandlerRegistry([]).Find(Inspection, Entry(OrderActionType.Approve)).Should().BeNull();
    }

    [Fact]
    public void Find_ReturnsFirstClaimant_InRegistrationOrder()
    {
        var first = new FirstHandler((_, _) => true);
        var second = new SecondHandler((_, _) => true);

        new OrderActionHandlerRegistry([first, second]).Find(Inspection, Entry(OrderActionType.Approve)).Should().BeSameAs(first);
        new OrderActionHandlerRegistry([second, first]).Find(Inspection, Entry(OrderActionType.Approve)).Should().BeSameAs(second);
    }

    [Fact]
    public void Find_NoClaimant_ReturnsNull()
    {
        var handler = new FirstHandler((_, action) => action == OrderActionType.Approve);

        new OrderActionHandlerRegistry([handler]).Find(Inspection, Entry(OrderActionType.Reject)).Should().BeNull();
    }

    [Fact]
    public void Find_PassesTheOrderTypeAndEntryToTheHandlers()
    {
        var handler = new FirstHandler((type, action) => string.Equals(type?.Id, "maintenance", StringComparison.Ordinal) && action == OrderActionType.Cancel);
        var registry = new OrderActionHandlerRegistry([handler]);

        registry.Find(Maintenance, Entry(OrderActionType.Cancel)).Should().BeSameAs(handler);
        registry.Find(Inspection, Entry(OrderActionType.Cancel)).Should().BeNull();
        registry.Find(null, Entry(OrderActionType.Cancel)).Should().BeNull();
    }

    [Fact]
    public void Find_NullEntry_Throws()
    {
        var act = () => new OrderActionHandlerRegistry([]).Find(Inspection, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullHandlers_Throws()
    {
        var act = () => new OrderActionHandlerRegistry(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EnsureUnambiguous_NullOrderTypes_Throws()
    {
        var act = () => new OrderActionHandlerRegistry([]).EnsureUnambiguous(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EnsureUnambiguous_TwoHandlersClaimingTheSamePair_ThrowsNamingThem()
    {
        var registry = new OrderActionHandlerRegistry(
        [
            new FirstHandler((type, action) => string.Equals(type?.Id, "maintenance", StringComparison.Ordinal) && action == OrderActionType.Approve),
            new SecondHandler((type, action) => string.Equals(type?.Id, "maintenance", StringComparison.Ordinal) && action == OrderActionType.Approve),
        ]);

        var act = () => registry.EnsureUnambiguous([Inspection, Maintenance]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{typeof(FirstHandler).FullName}*{typeof(SecondHandler).FullName}*Approve*maintenance*");
    }

    [Fact]
    public void EnsureUnambiguous_DisjointHandlers_DoesNotThrow()
    {
        var registry = new OrderActionHandlerRegistry(
        [
            new FirstHandler((_, action) => action == OrderActionType.Approve),
            new SecondHandler((_, action) => action == OrderActionType.Reject),
        ]);

        var act = () => registry.EnsureUnambiguous([Inspection, Maintenance]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureUnambiguous_SameActionOnDifferentOrderTypes_DoesNotThrow()
    {
        var registry = new OrderActionHandlerRegistry(
        [
            new FirstHandler((type, action) => string.Equals(type?.Id, "inspection", StringComparison.Ordinal) && action == OrderActionType.Approve),
            new SecondHandler((type, action) => string.Equals(type?.Id, "maintenance", StringComparison.Ordinal) && action == OrderActionType.Approve),
        ]);

        var act = () => registry.EnsureUnambiguous([Inspection, Maintenance]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureUnambiguous_SingleHandler_DoesNotProbe()
    {
        var only = new FirstHandler((_, _) => true);

        new OrderActionHandlerRegistry([only]).EnsureUnambiguous([Inspection, Maintenance]);

        only.Probes.Should().Be(0);
    }
}
