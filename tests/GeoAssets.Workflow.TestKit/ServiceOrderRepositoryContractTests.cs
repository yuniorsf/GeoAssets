using FluentAssertions;
using GeoAssets.Workflow.Orders;
using Xunit;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Reusable correctness contract for any <see cref="IServiceOrderRepository"/> implementation —
/// verifies transition-legality enforcement on <see cref="IServiceOrderWriter.UpdateAsync"/>/
/// <see cref="IServiceOrderWriter.AppendActionAsync"/> and <see cref="IServiceOrder.ChildOrderIds"/>
/// derivation from <see cref="IServiceOrder.ParentOrderId"/> on every read. Both rules were
/// previously enforced only by convention (see <c>ValidatingServiceOrderRepository</c> and
/// <c>ServiceOrder.md</c> §7/§16) — this base class turns them into a mechanically-checked
/// contract that any implementation can opt into by subclassing and implementing
/// <see cref="CreateRepository"/>. See XD01-27.
/// </summary>
public abstract class ServiceOrderRepositoryContractTests
{
    protected abstract IServiceOrderRepository CreateRepository();

    private static ServiceOrder Order(
        string id,
        string status = ServiceOrderStatus.Draft,
        string createdBy = "u1",
        string orderTypeId = "inspection",
        string? parentOrderId = null,
        DateTime? createdAt = null) => new()
    {
        Id            = id,
        Status        = status,
        CreatedBy     = createdBy,
        OrderTypeId   = orderTypeId,
        ParentOrderId = parentOrderId,
        CreatedAt     = createdAt ?? DateTime.UtcNow,
    };

    [Fact]
    public async Task UpdateAsync_IllegalTransition_ThrowsInvalidServiceOrderTransitionException()
    {
        var repository = CreateRepository();
        var order = Order("o1", status: ServiceOrderStatus.Draft);
        await repository.AddAsync(order);

        var illegal = Order("o1", status: ServiceOrderStatus.Completed);
        var act = async () => await repository.UpdateAsync(illegal);

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();

        var reloaded = await repository.GetByIdAsync("o1");
        reloaded!.Status.Should().Be(ServiceOrderStatus.Draft);
    }

    [Fact]
    public async Task AppendActionAsync_IllegalResultingStatus_ThrowsInvalidServiceOrderTransitionException()
    {
        var repository = CreateRepository();
        var order = Order("o1", status: ServiceOrderStatus.Draft);
        await repository.AddAsync(order);

        var entry = new OrderActionLog(
            OrderActionType.Complete,
            PerformedBy: "u1",
            PerformedAt: DateTime.UtcNow,
            ResultingStatus: ServiceOrderStatus.Completed);

        var act = async () => await repository.AppendActionAsync("o1", entry);

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();

        var reloaded = await repository.GetByIdAsync("o1");
        reloaded!.Status.Should().Be(ServiceOrderStatus.Draft);
        reloaded.ActionLog.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_LegalTransition_Succeeds()
    {
        var repository = CreateRepository();
        var order = Order("o1", status: ServiceOrderStatus.Draft);
        await repository.AddAsync(order);

        var updated = Order("o1", status: ServiceOrderStatus.Pending);
        await repository.UpdateAsync(updated);

        var reloaded = await repository.GetByIdAsync("o1");
        reloaded!.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    [Fact]
    public async Task AppendActionAsync_LegalResultingStatus_Succeeds()
    {
        var repository = CreateRepository();
        var order = Order("o1", status: ServiceOrderStatus.Draft);
        await repository.AddAsync(order);

        var entry = new OrderActionLog(
            OrderActionType.Approve,
            PerformedBy: "u1",
            PerformedAt: DateTime.UtcNow,
            ResultingStatus: ServiceOrderStatus.Pending);

        await repository.AppendActionAsync("o1", entry);

        var reloaded = await repository.GetByIdAsync("o1");
        reloaded!.Status.Should().Be(ServiceOrderStatus.Pending);
    }

    [Fact]
    public async Task GetByIdAsync_AfterAddingChild_ChildOrderIdsReflectsChild()
    {
        var repository = CreateRepository();
        await repository.AddAsync(Order("parent"));
        await repository.AddAsync(Order("child", parentOrderId: "parent"));

        var parent = await repository.GetByIdAsync("parent");

        parent!.ChildOrderIds.Should().BeEquivalentTo(["child"]);
    }

    [Fact]
    public async Task GetByIdAsync_NoChildren_ChildOrderIdsIsEmpty()
    {
        var repository = CreateRepository();
        await repository.AddAsync(Order("lonely"));

        var order = await repository.GetByIdAsync("lonely");

        order!.ChildOrderIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllAsync_PopulatesChildOrderIdsForEveryOrder()
    {
        var repository = CreateRepository();
        await repository.AddAsync(Order("parent"));
        await repository.AddAsync(Order("child1", parentOrderId: "parent"));
        await repository.AddAsync(Order("child2", parentOrderId: "parent"));

        var all = await repository.GetAllAsync();

        all.Single(o => o.Id == "parent").ChildOrderIds.Should().BeEquivalentTo(["child1", "child2"]);
        all.Single(o => o.Id == "child1").ChildOrderIds.Should().BeEmpty();
        all.Single(o => o.Id == "child2").ChildOrderIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRootsAsync_PopulatesChildOrderIdsForRoots()
    {
        var repository = CreateRepository();
        await repository.AddAsync(Order("parent"));
        await repository.AddAsync(Order("child1", parentOrderId: "parent"));
        await repository.AddAsync(Order("child2", parentOrderId: "parent"));

        var roots = await repository.GetRootsAsync();

        var root = roots.Should().ContainSingle().Subject;
        root.Id.Should().Be("parent");
        root.ChildOrderIds.Should().BeEquivalentTo(["child1", "child2"]);
    }
}
