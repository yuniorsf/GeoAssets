using FluentAssertions;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Tests.Orders;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Isp.NeutralOperator;

public class ClandestineAttachmentServiceTests
{
    private static ServiceOrder NewActiveOrder(string id = "order-1") => new()
    {
        Id = id,
        OrderTypeId = ClandestineAttachmentOrderTypes.ClandestineNoticeId,
        Status = ClandestineAttachmentWorkflowGraph.Active,
    };

    private static async Task<(FakeServiceOrderRepository Repo, FakeTimeProvider Time, ClandestineAttachmentService Service)> SetUpAsync(ServiceOrder order)
    {
        var repo = new FakeServiceOrderRepository();
        await repo.AddAsync(order);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        return (repo, time, new ClandestineAttachmentService(repo, time));
    }

    [Fact]
    public async Task FlagAsync_FromActive_TransitionsToCaboARevelia_AndStamps30DayDeadline()
    {
        var order = NewActiveOrder();
        var (repo, time, service) = await SetUpAsync(order);

        var result = await service.FlagAsync(order.Id, "inspector-1");

        result.Status.Should().Be(ClandestineAttachmentWorkflowGraph.CaboARevelia);
        result.FlaggedAtUtc.Should().Be(time.GetUtcNow());
        result.DeadlineUtc.Should().Be(time.GetUtcNow() + TimeSpan.FromDays(30));

        var reloaded = await repo.GetByIdAsync(order.Id);
        reloaded!.Status.Should().Be(ClandestineAttachmentWorkflowGraph.CaboARevelia);
        reloaded.ActionLog.Should().ContainSingle(a => a.Action == OrderActionType.FlagClandestine);
    }

    [Fact]
    public async Task FlagAsync_WhenNotActive_Throws()
    {
        var order = NewActiveOrder();
        order.Status = ClandestineAttachmentWorkflowGraph.CaboARevelia;
        var (_, _, service) = await SetUpAsync(order);

        var act = () => service.FlagAsync(order.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RegularizeAsync_WithinWindow_TransitionsToRegularized()
    {
        var order = NewActiveOrder();
        var (repo, time, service) = await SetUpAsync(order);
        await service.FlagAsync(order.Id);

        time.Advance(TimeSpan.FromDays(29));
        var result = await service.RegularizeAsync(order.Id, "operator-1");

        result.Status.Should().Be(ClandestineAttachmentWorkflowGraph.Regularized);
        var reloaded = await repo.GetByIdAsync(order.Id);
        reloaded!.Status.Should().Be(ClandestineAttachmentWorkflowGraph.Regularized);
    }

    [Fact]
    public async Task RegularizeAsync_AfterWindowElapsed_ThrowsAndDoesNotTransition()
    {
        var order = NewActiveOrder();
        var (repo, time, service) = await SetUpAsync(order);
        await service.FlagAsync(order.Id);

        time.Advance(TimeSpan.FromDays(31));
        var act = () => service.RegularizeAsync(order.Id, "operator-1");

        await act.Should().ThrowAsync<ClandestineNoticeWindowElapsedException>();
        var reloaded = await repo.GetByIdAsync(order.Id);
        reloaded!.Status.Should().Be(ClandestineAttachmentWorkflowGraph.CaboARevelia,
            "a late regularization attempt must not silently succeed");
    }

    [Fact]
    public async Task RegularizeAsync_WhenNotFlagged_Throws()
    {
        var order = NewActiveOrder();
        var (_, _, service) = await SetUpAsync(order);

        var act = () => service.RegularizeAsync(order.Id);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetElapsedUnregularizedAsync_ReturnsOnlyFlaggedOrdersPastDeadline()
    {
        var elapsedOrder = NewActiveOrder("order-elapsed");
        var withinWindowOrder = NewActiveOrder("order-within-window");
        var regularizedOrder = NewActiveOrder("order-regularized");
        var repo = new FakeServiceOrderRepository();
        await repo.AddAsync(elapsedOrder);
        await repo.AddAsync(withinWindowOrder);
        await repo.AddAsync(regularizedOrder);

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new ClandestineAttachmentService(repo, time);

        await service.FlagAsync(elapsedOrder.Id);
        await service.FlagAsync(regularizedOrder.Id);
        await service.RegularizeAsync(regularizedOrder.Id);

        time.Advance(TimeSpan.FromDays(31));
        await service.FlagAsync(withinWindowOrder.Id);

        var elapsed = await service.GetElapsedUnregularizedAsync(time.GetUtcNow());

        elapsed.Should().ContainSingle(r => r.ServiceOrderId == elapsedOrder.Id);
    }

    [Fact]
    public async Task DisputeAsync_FromCaboARevelia_TransitionsToDisputed_AndStoresEvidenceReference()
    {
        var order = NewActiveOrder();
        var (repo, _, service) = await SetUpAsync(order);
        await service.FlagAsync(order.Id);

        var result = await service.DisputeAsync(order.Id, "receipt-digest-abc123", "tenant-user-1");

        result.Status.Should().Be(ClandestineAttachmentWorkflowGraph.Disputed);
        var reloaded = await repo.GetByIdAsync(order.Id);
        reloaded!.Status.Should().Be(ClandestineAttachmentWorkflowGraph.Disputed);
        reloaded.Attributes[ClandestineAttachmentService.AttrDisputeEvidenceReference].Should().Be("receipt-digest-abc123");
        reloaded.ActionLog.Should().ContainSingle(a => a.Action == OrderActionType.Dispute);
    }

    [Fact]
    public async Task DisputeAsync_PastTheStatutoryDeadline_StillSucceeds()
    {
        // Contesting that the flag was ever valid is a different claim from having fixed it —
        // unlike RegularizeAsync, elapsed time must not block a dispute.
        var order = NewActiveOrder();
        var (_, time, service) = await SetUpAsync(order);
        await service.FlagAsync(order.Id);
        time.Advance(TimeSpan.FromDays(45));

        var result = await service.DisputeAsync(order.Id, "receipt-digest-abc123");

        result.Status.Should().Be(ClandestineAttachmentWorkflowGraph.Disputed);
    }

    [Fact]
    public async Task DisputeAsync_WhenNotFlagged_Throws()
    {
        var order = NewActiveOrder();
        var (_, _, service) = await SetUpAsync(order);

        var act = () => service.DisputeAsync(order.Id, "receipt-digest-abc123");

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetActiveFlaggedForOrganizationAsync_ReturnsOnlyThatOrganizationsFlaggedOrders()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var flaggedForA = new ServiceOrder
        {
            Id = "order-a",
            OrderTypeId = ClandestineAttachmentOrderTypes.ClandestineNoticeId,
            Status = ClandestineAttachmentWorkflowGraph.Active,
            OrganizationId = orgA,
        };
        var flaggedForB = new ServiceOrder
        {
            Id = "order-b",
            OrderTypeId = ClandestineAttachmentOrderTypes.ClandestineNoticeId,
            Status = ClandestineAttachmentWorkflowGraph.Active,
            OrganizationId = orgB,
        };
        var regularizedForA = new ServiceOrder
        {
            Id = "order-a-regularized",
            OrderTypeId = ClandestineAttachmentOrderTypes.ClandestineNoticeId,
            Status = ClandestineAttachmentWorkflowGraph.Active,
            OrganizationId = orgA,
        };

        var repo = new FakeServiceOrderRepository();
        await repo.AddAsync(flaggedForA);
        await repo.AddAsync(flaggedForB);
        await repo.AddAsync(regularizedForA);

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        var service = new ClandestineAttachmentService(repo, time);
        await service.FlagAsync(flaggedForA.Id);
        await service.FlagAsync(flaggedForB.Id);
        await service.FlagAsync(regularizedForA.Id);
        await service.RegularizeAsync(regularizedForA.Id);

        var result = await service.GetActiveFlaggedForOrganizationAsync(orgA);

        result.Should().ContainSingle(o => o.Id == flaggedForA.Id,
            "orgB's flagged order and orgA's already-regularized order must both be excluded");
    }
}
