using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Behavioural contract of the shared-use dispatch (XD01-316) — <see cref="ISharedUseDispatchService"/>
/// and <see cref="ISharedUseDispatchPlanReader"/> — over whatever stores a runner puts under it.
/// The same facts run on the in-memory TestKit stores, on EF Core over SQLite and on EF Core
/// over PostgreSQL, so "a fan-out" means one thing on all three.
///
/// Subclass it, implement <see cref="CreateHarnessAsync"/>, and declare at least one
/// <c>[Fact]</c> of your own on the subclass (xUnit only discovers <c>public</c> classes).
///
/// The facts are split over two files: dispatching (here) and the plan reads, refusals and
/// blocked orders (<c>.Plans.cs</c>).
/// </summary>
public abstract partial class SharedUseDispatchContractTests
{
    protected const string PoleType = "pole";
    protected const string Activity = "relocate-attachment";

    /// <summary>An order type whose orders block every other order from their poles.</summary>
    protected const string ExclusiveTypeId = "exclusive-pole-work";

    protected static readonly Guid OccupantOne = new("0c0c0c0c-0000-4000-8000-000000000001");
    protected static readonly Guid OccupantTwo = new("0c0c0c0c-0000-4000-8000-000000000002");
    protected static readonly Guid OccupantThree = new("0c0c0c0c-0000-4000-8000-000000000003");

    private static readonly DateTime CreatedAt = new(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A fresh dispatch over empty stores, all scoped to one pole-owner organization.</summary>
    protected abstract Task<SharedUseDispatchContractHarness> CreateHarnessAsync();

    // ── Arrange helpers ───────────────────────────────────────────────────────

    /// <summary>A member of the pole owner's organization who may dispatch.</summary>
    protected static WorkflowPrincipal Dispatcher(SharedUseDispatchContractHarness harness)
    {
        ArgumentNullException.ThrowIfNull(harness);
        return new WorkflowPrincipal("dispatcher-1", harness.OwnerOrganizationId.ToString(), ["Administrator"], [], []);
    }

    /// <summary>Registers the shared-use types, and the exclusive type, with the harness's registry.</summary>
    protected static void RegisterTypes(SharedUseDispatchContractHarness harness)
    {
        ArgumentNullException.ThrowIfNull(harness);

        SharedUseOrderTypes.RegisterAll(harness.OrderTypes, [PoleType]);
        harness.OrderTypes.Register(new OrderType
        {
            Id = ExclusiveTypeId,
            DisplayName = "Exclusive pole work",
            TargetPolicy = new OrderTargetPolicy
            {
                TargetAssetTypeIds = [PoleType],
                Exclusivity = OrderTargetExclusivity.BlockOnOverlap,
            },
        });
    }

    /// <summary>A pole-work order of the owner holding one target per pole, each with one activity.</summary>
    protected static async Task SeedOrderAsync(
        SharedUseDispatchContractHarness harness, string orderId, string status, params string[] poles)
    {
        ArgumentNullException.ThrowIfNull(harness);
        RegisterTypes(harness);

        await harness.Orders.AddAsync(new ServiceOrder
        {
            Id = orderId,
            Title = $"Shared use {orderId}",
            Description = "Clear the lane for the new feeder.",
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            Status = status,
            Priority = ServiceOrderPriority.High,
            CreatedBy = "planner-1",
            OrganizationId = harness.OwnerOrganizationId,
            CreatedAt = CreatedAt,
        });
        await AddTargetsAsync(harness, orderId, [Activity], poles);
    }

    /// <summary>Adds one target per pole to an existing order, with the given activities.</summary>
    protected static Task<int> AddTargetsAsync(
        SharedUseDispatchContractHarness harness, string orderId, string[] activities, params string[] poles)
    {
        ArgumentNullException.ThrowIfNull(harness);
        ArgumentNullException.ThrowIfNull(poles);

        return harness.TargetWriter.AddRangeAsync(
            orderId,
            [.. poles.Select(pole => new ServiceOrderTargetBuilder()
                .WithId(OrderTargetSelectionService.TargetId(orderId, pole))
                .WithOrderId(orderId)
                .WithFeatureId(pole)
                .WithAssetTypeId(PoleType)
                .WithAlias($"B-{pole}")
                .WithActivityIds(activities)
                .WithOrganizationId(harness.OwnerOrganizationId)
                .Build())],
            CancellationToken.None);
    }

    /// <summary>An entry on a pole of the owner; contracted and identified unless said otherwise.</summary>
    protected static PoleOccupancyEntry Entry(
        SharedUseDispatchContractHarness harness, string pole, int? slot, Guid? occupant,
        PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.AuthorizedContracted)
    {
        ArgumentNullException.ThrowIfNull(harness);

        var builder = new PoleOccupancyEntryBuilder().ForPole(pole).OwnedBy(harness.OwnerOrganizationId).WithStatus(status).OccupiedBy(occupant);
        return (slot is { } index ? builder.OnSlot(index) : builder.BeyondRegulatedSlots()).Build();
    }

    /// <summary>Puts a pole's occupancy on file (its first write).</summary>
    protected static Task OccupyAsync(SharedUseDispatchContractHarness harness, string pole, params PoleOccupancyEntry[] entries)
    {
        ArgumentNullException.ThrowIfNull(harness);
        return harness.Occupancy.ReplaceEntriesAsync(pole, PoleOccupancy.UnsavedVersion, entries, CancellationToken.None);
    }

    /// <summary>Dispatches as <see cref="Dispatcher"/>, with the row version the order has right now.</summary>
    protected static async Task<SharedUseDispatchResult> DispatchAsync(SharedUseDispatchContractHarness harness, string orderId)
    {
        ArgumentNullException.ThrowIfNull(harness);

        var order = (await harness.Orders.GetByIdAsync(orderId))!;
        return await harness.Service.DispatchAsync(orderId, order.RowVersion, Dispatcher(harness), CancellationToken.None);
    }

    /// <summary>The pole feature ids a child order holds, in order.</summary>
    protected static async Task<List<string>> PolesOfAsync(SharedUseDispatchContractHarness harness, string orderId)
    {
        ArgumentNullException.ThrowIfNull(harness);

        var page = await harness.TargetReader.GetPageAsync(new OrderTargetQuery(orderId, pageSize: OrderTargetQuery.MaxPageSize), CancellationToken.None);
        return [.. page.Items.Select(t => t.FeatureId).Order(StringComparer.Ordinal)];
    }

    // ── Dispatching ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DispatchAsync_CreatesOneChildOrderPerContractedOccupant_HoldingOnlyThatOccupantsPoles()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1", "pole-2", "pole-3");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        await OccupyAsync(harness, "pole-2", Entry(harness, "pole-2", 1, OccupantOne), Entry(harness, "pole-2", 2, OccupantTwo));
        await OccupyAsync(harness, "pole-3", Entry(harness, "pole-3", 4, OccupantTwo));

        var result = await DispatchAsync(harness, "ord-1");

        result.Should().BeEquivalentTo(new SharedUseDispatchResult
        {
            ParentOrderId = "ord-1",
            OrderTypeId = SharedUseOrderTypes.PoleWorkId,
            ParentStatus = ServiceOrderStatus.Pending,
            ChildOrdersCreated = 2,
            ChildOrdersAlreadyDispatched = 0,
            NotificationsEnqueued = 2,
            TargetCount = 3,
            BlockedTargetCount = 0,
            UnnotifiablePoleCount = 0,
            SkippedEntryCount = 0,
        });
        var children = await harness.Orders.GetChildrenAsync("ord-1");
        children.Select(c => c.Id).Should().BeEquivalentTo(
            [SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantOne), SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantTwo)]);
        (await PolesOfAsync(harness, SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantOne))).Should().Equal("pole-1", "pole-2");
        (await PolesOfAsync(harness, SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantTwo))).Should().Equal("pole-2", "pole-3");
    }

    [Fact]
    public async Task DispatchAsync_AChildIsOwnedByThePoleOwner_DispatchedToItsOccupant_AndCarriesTheParentsActivities()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        var now = harness.Clock.GetUtcNow();

        await DispatchAsync(harness, "ord-1");

        var childId = SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantOne);
        var child = (await harness.Orders.GetByIdAsync(childId))!;
        child.OrganizationId.Should().Be(harness.OwnerOrganizationId, "the notice stays the pole owner's");
        child.ParentOrderId.Should().Be("ord-1");
        child.OrderTypeId.Should().Be(SharedUseOrderTypes.OccupantNoticeId);
        child.Status.Should().Be(ServiceOrderStatus.Pending);
        child.Title.Should().Be("Shared use ord-1");
        child.Priority.Should().Be(ServiceOrderPriority.High);
        child.CreatedBy.Should().Be("dispatcher-1");
        child.CreatedAt.Should().Be(now.UtcDateTime);
        child.Attributes[SharedUseDispatchPlanner.PartyOrganizationAttribute].Should().Be(OccupantOne.ToString("D"));
        var dispatch = child.Dispatches.Should().ContainSingle().Subject;
        dispatch.TargetType.Should().Be(DispatchTargetType.Organization);
        dispatch.TargetId.Should().Be(OccupantOne.ToString("D"));
        dispatch.DispatchedBy.Should().Be("dispatcher-1");
        dispatch.DispatchedAt.Should().Be(now.UtcDateTime);

        var target = (await harness.TargetReader.GetPageAsync(new OrderTargetQuery(childId), CancellationToken.None)).Items.Should().ContainSingle().Subject;
        target.Id.Should().Be(OrderTargetSelectionService.TargetId(childId, "pole-1"));
        target.ActivityIds.Should().Equal(Activity);
        target.Alias.Should().Be("B-pole-1");
        target.OrganizationId.Should().Be(harness.OwnerOrganizationId);
        target.Status.Should().Be(OrderTargetStatus.Pending);
        target.AddedBy.Should().Be("dispatcher-1");
    }

    [Fact]
    public async Task DispatchAsync_MovesTheParentToDispatched_LogsOneDispatch_AndKeepsTheParent()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1", "pole-2");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        await OccupyAsync(harness, "pole-2", Entry(harness, "pole-2", 1, OccupantTwo));

        await DispatchAsync(harness, "ord-1");

        var parent = (await harness.Orders.GetByIdAsync("ord-1"))!;
        parent.Status.Should().Be(ServiceOrderStatus.Pending);
        parent.OrganizationId.Should().Be(harness.OwnerOrganizationId);
        parent.Dispatches.Should().BeEmpty("the parent is dispatched through its children, not to anyone itself");
        var entry = parent.ActionLog.Should().ContainSingle().Subject;
        entry.Action.Should().Be(OrderAction.Dispatch);
        entry.PerformedBy.Should().Be("dispatcher-1");
        entry.Comment.Should().Contain("2");
        (await PolesOfAsync(harness, "ord-1")).Should().Equal("pole-1", "pole-2");
    }

    [Fact]
    public async Task DispatchAsync_StagesOneNotificationPerChild_CorrelatedToTheParent()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne), Entry(harness, "pole-1", 2, OccupantTwo));
        var now = harness.Clock.GetUtcNow();

        await DispatchAsync(harness, "ord-1");

        var notifications = await harness.StagedNotificationsAsync(CancellationToken.None);
        notifications.Should().HaveCount(2);
        notifications.Select(n => n.OrderId).Should().BeEquivalentTo(
            [SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantOne), SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantTwo)]);
        foreach (var notification in notifications)
        {
            notification.MessageType.Should().Be(IntegrationEventTypes.OrderStateChangedV1);
            notification.PartitionKey.Should().Be(notification.OrderId);
            notification.CorrelationId.Should().Be("ord-1");
            notification.CausationId.Should().Be(SharedUseDispatchService.CausationId("ord-1"));
            notification.OrderTypeId.Should().Be(SharedUseOrderTypes.OccupantNoticeId);
            notification.NewStatus.Should().Be(ServiceOrderStatus.Pending);
            notification.PerformedBy.Should().Be("dispatcher-1");
            notification.OccurredAt.Should().Be(now);
            notification.Metadata[SharedUseDispatchService.ParentOrderMetadataKey].Should().Be("ord-1");
        }

        notifications.Select(n => n.Metadata[SharedUseDispatchService.PartyOrganizationMetadataKey]).Should().BeEquivalentTo(
            [OccupantOne.ToString("D"), OccupantTwo.ToString("D")]);
    }

    [Fact]
    public async Task DispatchAsync_Repeated_CreatesNothingNew_NotifiesNobodyAgain_AndLogsNothingMore()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne), Entry(harness, "pole-1", 2, OccupantTwo));
        await DispatchAsync(harness, "ord-1");

        var again = await DispatchAsync(harness, "ord-1");

        again.ChildOrdersCreated.Should().Be(0);
        again.ChildOrdersAlreadyDispatched.Should().Be(2);
        again.NotificationsEnqueued.Should().Be(0);
        again.ParentStatus.Should().Be(ServiceOrderStatus.Pending);
        (await harness.Orders.GetChildrenAsync("ord-1")).Should().HaveCount(2);
        (await harness.StagedNotificationsAsync(CancellationToken.None)).Should().HaveCount(2);
        (await harness.Orders.GetByIdAsync("ord-1"))!.ActionLog.Should().ContainSingle();
    }

    [Fact]
    public async Task DispatchAsync_AfterPolesWereAdded_OnlyTheNewOccupantGetsAChild_AndAnExistingChildIsLeftAsItWas()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        await OccupyAsync(harness, "pole-2", Entry(harness, "pole-2", 1, OccupantOne), Entry(harness, "pole-2", 2, OccupantThree));
        await DispatchAsync(harness, "ord-1");
        await AddTargetsAsync(harness, "ord-1", [Activity], "pole-2");

        var second = await DispatchAsync(harness, "ord-1");

        second.ChildOrdersCreated.Should().Be(1);
        second.ChildOrdersAlreadyDispatched.Should().Be(1);
        (await PolesOfAsync(harness, SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantThree))).Should().Equal("pole-2");
        (await PolesOfAsync(harness, SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantOne)))
            .Should().Equal(["pole-1"], "a child that exists is not changed by a later dispatch");
        (await harness.StagedNotificationsAsync(CancellationToken.None)).Should().HaveCount(2);
        (await harness.Orders.GetByIdAsync("ord-1"))!.ActionLog.Should().HaveCount(2);
    }

    [Fact]
    public async Task DispatchAsync_AContractedEntryBeyondTheSixSlots_IsAnOccupantLikeAnyOther()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        var full = Enumerable.Range(1, 6).Select(slot => Entry(harness, "pole-1", slot, OccupantOne));
        await OccupyAsync(harness, "pole-1", [.. full, Entry(harness, "pole-1", slot: null, OccupantTwo), Entry(harness, "pole-1", slot: null, OccupantThree)]);

        var result = await DispatchAsync(harness, "ord-1");

        result.ChildOrdersCreated.Should().Be(3, "the seventh and eighth attachments are real occupants");
        (await PolesOfAsync(harness, SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantTwo))).Should().Equal("pole-1");
        (await PolesOfAsync(harness, SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantThree))).Should().Equal("pole-1");
        (await PolesOfAsync(harness, SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantOne)))
            .Should().Equal(["pole-1"], "six entries of one occupant on one pole are one pole");
    }

    [Fact]
    public async Task DispatchAsync_LeavesOutATargetAnotherOpenOrderBlocks_AndStillDispatchesTheRest()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1", "pole-2");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        await OccupyAsync(harness, "pole-2", Entry(harness, "pole-2", 1, OccupantOne), Entry(harness, "pole-2", 2, OccupantTwo));
        await harness.Orders.AddAsync(new ServiceOrder
        {
            Id = "ord-exclusive",
            Title = "Exclusive",
            OrderTypeId = ExclusiveTypeId,
            CreatedBy = "planner-1",
            OrganizationId = harness.OwnerOrganizationId,
            CreatedAt = CreatedAt,
        });
        await AddTargetsAsync(harness, "ord-exclusive", [], "pole-2");

        var result = await DispatchAsync(harness, "ord-1");

        result.BlockedTargetCount.Should().Be(1);
        result.ChildOrdersCreated.Should().Be(1, "the occupant found only on the blocked pole gets nothing");
        (await PolesOfAsync(harness, SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantOne))).Should().Equal("pole-1");
        (await harness.Orders.GetByIdAsync(SharedUseDispatchPlanner.ChildOrderId("ord-1", OccupantTwo))).Should().BeNull();
    }

    [Fact]
    public async Task DispatchAsync_ItsOwnChildrenHoldingTheSamePoles_NeverBlockADispatchAgain()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1", ServiceOrderStatus.Draft, "pole-1");
        await OccupyAsync(harness, "pole-1", Entry(harness, "pole-1", 1, OccupantOne));
        await DispatchAsync(harness, "ord-1");

        var preview = await harness.Plans.PreviewAsync("ord-1", 1, 50, Dispatcher(harness), CancellationToken.None);

        preview.BlockedTargetCount.Should().Be(0, "an order's own children are its family, not a conflict");
        preview.BlockReason.Should().BeNull();
        preview.Recipients.Items.Should().ContainSingle().Which.AlreadyDispatched.Should().BeTrue();
    }
}
