using FluentAssertions;
using GeoAssets.Workflow.Contractors;
using GeoAssets.Workflow.FieldWork;
using GeoAssets.Workflow.Messaging.Protobuf;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Persistence.Entities;
using GeoAssets.Workflow.Persistence.Outbox;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// XD01-306: an order that reaches a terminal state releases its targets — <c>ReleasedAt</c> is
/// stamped from the injected clock in the same <c>SaveChanges</c> as the status change — on
/// every write path that can change an order's status: <see cref="EFServiceOrderRepository.UpdateAsync"/>,
/// <see cref="EFServiceOrderRepository.AppendActionAsync"/> and, through the former,
/// <see cref="InspectionCommitService"/>. Any other write leaves targets untouched.
/// </summary>
public sealed class ServiceOrderTargetReleaseTests : IDisposable
{
    private static readonly DateTimeOffset AddedAt = new(2026, 7, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ClosedAtInstant = new(2026, 7, 2, 17, 30, 0, TimeSpan.Zero);
    private static readonly DateTime ClosedAt = ClosedAtInstant.UtcDateTime;

    private readonly SqliteFixture _fixture = new();
    private readonly OrderTypeRegistry _registry = new();
    private readonly FakeTimeProvider _clock = new(AddedAt);
    private readonly EFServiceOrderTargetRepository _targets;

    public ServiceOrderTargetReleaseTests() =>
        _targets = new EFServiceOrderTargetRepository(_fixture.Context, _registry, _clock);

    /// <summary>The order repository under test, on the fixture's context and the test's clock.</summary>
    private EFServiceOrderRepository NewOrders() =>
        new(_fixture.Context, orderTypeRegistry: _registry, timeProvider: _clock);

    public void Dispose() => _fixture.Dispose();

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_IntoATerminalState_StampsEveryUnreleasedTargetOfThatOrderWithTheClock()
    {
        await SeedOrderWithTargetsAsync("ord-closing", "f1", "f2", "f3");
        await SeedOrderWithTargetsAsync("ord-other", "f1");
        _clock.SetUtcNow(ClosedAtInstant);

        await UpdateStatusAsync("ord-closing", ServiceOrderStatus.Cancelled);

        var released = await ReleasedAtByTargetAsync();
        released.Where(t => t.Key.StartsWith("ord-closing", StringComparison.Ordinal)).Select(t => t.Value)
            .Should().HaveCount(3).And.OnlyContain(stamp => stamp == ClosedAt);
        released["ord-other:f1"].Should().BeNull("only the closing order's targets are released");
    }

    [Fact]
    public async Task UpdateAsync_IntoATerminalState_WritesTheStatusAndTheReleaseStampsInOneSaveChanges()
    {
        await SeedOrderWithTargetsAsync("ord-1", "f1", "f2");
        var saves = new List<string[]>();
        _fixture.Context.SavingChanges += (_, _) => saves.Add(
            _fixture.Context.ChangeTracker.Entries()
                .Where(entry => entry.State == EntityState.Modified)
                .Select(entry => entry.Metadata.ClrType.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());

        await UpdateStatusAsync("ord-1", ServiceOrderStatus.Cancelled);

        saves.Should().ContainSingle().Which.Should().Equal(
            nameof(ServiceOrderRecord), nameof(ServiceOrderTargetRecord), nameof(ServiceOrderTargetRecord));
    }

    [Fact]
    public async Task UpdateAsync_IntoANonTerminalState_LeavesTargetsUntouched()
    {
        await SeedOrderWithTargetsAsync("ord-1", "f1", "f2");
        _clock.SetUtcNow(ClosedAtInstant);

        await UpdateStatusAsync("ord-1", ServiceOrderStatus.Pending);
        await UpdateStatusAsync("ord-1", ServiceOrderStatus.InProgress);
        await UpdateStatusAsync("ord-1", ServiceOrderStatus.OnHold);

        (await ReleasedAtByTargetAsync()).Values.Should().OnlyContain(stamp => stamp == null);
    }

    [Fact]
    public async Task UpdateAsync_ThatKeepsTheStatus_LeavesTargetsUntouched()
    {
        await SeedOrderWithTargetsAsync("ord-1", "f1");
        _clock.SetUtcNow(ClosedAtInstant);
        var order = await LoadAsync("ord-1");
        order.Title = "renamed, same status";

        await NewOrders().UpdateAsync(order);

        (await ReleasedAtByTargetAsync())["ord-1:f1"].Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ThatKeepsATerminalStatus_DoesNotRestampAlreadyReleasedTargets()
    {
        await SeedOrderWithTargetsAsync("ord-1", "f1");
        _clock.SetUtcNow(ClosedAtInstant);
        await UpdateStatusAsync("ord-1", ServiceOrderStatus.Cancelled);
        _clock.SetUtcNow(ClosedAtInstant.AddDays(5));
        var order = await LoadAsync("ord-1");
        order.Description = "a note added after closing";

        await NewOrders().UpdateAsync(order);

        (await ReleasedAtByTargetAsync())["ord-1:f1"].Should().Be(ClosedAt);
    }

    [Fact]
    public async Task UpdateAsync_FromOneTerminalStateToAnother_KeepsTheFirstReleaseStamp()
    {
        // A success state is terminal even when the graph lets it move on (here to an archive state).
        _registry.Register(new OrderType
        {
            Id = "archivable",
            DisplayName = "Archivable",
            States = [new WorkflowState("Open", "Open"), new WorkflowState("Done", "Done", IsSuccess: true), new WorkflowState("Archived", "Archived")],
            Transitions = [new WorkflowTransition("Open", "Done"), new WorkflowTransition("Done", "Archived")],
            InitialStateKey = "Open",
        });
        await SeedTypedOrderWithTargetsAsync("ord-1", "archivable", "Open", "f1");
        _clock.SetUtcNow(ClosedAtInstant);
        await UpdateStatusAsync("ord-1", "Done");
        _clock.SetUtcNow(ClosedAtInstant.AddDays(30));

        await UpdateStatusAsync("ord-1", "Archived");

        (await ReleasedAtByTargetAsync())["ord-1:f1"].Should().Be(ClosedAt);
    }

    [Fact]
    public async Task UpdateAsync_IntoTheTerminalStateOfACustomGraph_ReleasesByThatGraphNotTheDefaultOne()
    {
        _registry.Register(new OrderType
        {
            Id = "custom",
            DisplayName = "Custom",
            // "Completed" is terminal in the default graph but only a waypoint here.
            States = [new WorkflowState("Open", "Open"), new WorkflowState(ServiceOrderStatus.Completed, "Completed"), new WorkflowState("Signed", "Signed")],
            Transitions = [new WorkflowTransition("Open", ServiceOrderStatus.Completed), new WorkflowTransition(ServiceOrderStatus.Completed, "Signed")],
            InitialStateKey = "Open",
        });
        await SeedTypedOrderWithTargetsAsync("ord-1", "custom", "Open", "f1");
        _clock.SetUtcNow(ClosedAtInstant);

        await UpdateStatusAsync("ord-1", ServiceOrderStatus.Completed);
        var afterWaypoint = (await ReleasedAtByTargetAsync())["ord-1:f1"];
        await UpdateStatusAsync("ord-1", "Signed");
        var afterDeadEnd = (await ReleasedAtByTargetAsync())["ord-1:f1"];

        afterWaypoint.Should().BeNull();
        afterDeadEnd.Should().Be(ClosedAt);
    }

    [Fact]
    public async Task UpdateAsync_IllegalTransition_ReleasesNothing()
    {
        await SeedOrderWithTargetsAsync("ord-1", "f1");

        var act = () => UpdateStatusAsync("ord-1", ServiceOrderStatus.Completed);

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        (await ReleasedAtByTargetAsync())["ord-1:f1"].Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_WhenTheSaveLosesAConcurrencyRace_ReleasesNothing_NotEvenOnALaterSave()
    {
        await SeedOrderWithTargetsAsync("ord-1", "f1", "f2");
        var stale = await LoadAsync("ord-1");
        using (var otherContext = _fixture.NewContext())
        {
            var otherWriter = new EFServiceOrderRepository(otherContext, orderTypeRegistry: _registry, timeProvider: _clock);
            var theirs = (ServiceOrder)(await otherWriter.GetByIdAsync("ord-1"))!;
            theirs.Title = "changed by another writer";
            await otherWriter.UpdateAsync(theirs);
        }

        stale.Status = ServiceOrderStatus.Cancelled;
        var act = () => NewOrders().UpdateAsync(stale);

        await act.Should().ThrowAsync<ServiceOrderConcurrencyException>();
        (await ReleasedAtByTargetAsync()).Values.Should().OnlyContain(stamp => stamp == null);
        _fixture.Context.ChangeTracker.Entries<ServiceOrderTargetRecord>()
            .Should().OnlyContain(entry => entry.State == EntityState.Unchanged && entry.Entity.ReleasedAt == null,
                "a stamp left pending in the change tracker would be written by this context's next save, releasing an order that never closed");
    }

    [Fact]
    public async Task UpdateAsync_OrderWithoutTargets_ClosesNormally()
    {
        await SeedOrderWithTargetsAsync("ord-1");

        await UpdateStatusAsync("ord-1", ServiceOrderStatus.Cancelled);

        (await LoadAsync("ord-1")).Status.Should().Be(ServiceOrderStatus.Cancelled);
    }

    // ── AppendActionAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task AppendActionAsync_WithATerminalResultingStatus_StampsTheOrdersTargetsInTheSameSaveChanges()
    {
        await SeedOrderWithTargetsAsync("ord-1", "f1", "f2");
        await SeedOrderWithTargetsAsync("ord-other", "f1");
        _clock.SetUtcNow(ClosedAtInstant);
        var saves = 0;
        _fixture.Context.SavingChanges += (_, _) => saves++;

        await NewOrders().AppendActionAsync("ord-1", new OrderActionLog(
            OrderActionType.Cancel, PerformedBy: "u1", PerformedAt: ClosedAt.AddMinutes(-5), ResultingStatus: ServiceOrderStatus.Cancelled));

        saves.Should().Be(1);
        var released = await ReleasedAtByTargetAsync();
        released["ord-1:f1"].Should().Be(ClosedAt, "the stamp comes from the clock, not from the action's PerformedAt");
        released["ord-1:f2"].Should().Be(ClosedAt);
        released["ord-other:f1"].Should().BeNull();
    }

    [Fact]
    public async Task AppendActionAsync_WithANonTerminalResultingStatus_LeavesTargetsUntouched()
    {
        await SeedOrderWithTargetsAsync("ord-1", "f1");

        await NewOrders().AppendActionAsync("ord-1", new OrderActionLog(
            OrderActionType.Approve, PerformedBy: "u1", PerformedAt: ClosedAt, ResultingStatus: ServiceOrderStatus.Pending));

        (await ReleasedAtByTargetAsync())["ord-1:f1"].Should().BeNull();
    }

    [Fact]
    public async Task AppendActionAsync_WithoutAResultingStatus_LeavesTargetsUntouched()
    {
        await SeedOrderWithTargetsAsync("ord-1", "f1");

        await NewOrders().AppendActionAsync("ord-1", new OrderActionLog(
            OrderActionType.Annotate, PerformedBy: "u1", PerformedAt: ClosedAt, Comment: "just a note"));

        (await ReleasedAtByTargetAsync())["ord-1:f1"].Should().BeNull();
    }

    [Fact]
    public async Task AppendActionAsync_IllegalResultingStatus_ReleasesNothing()
    {
        await SeedOrderWithTargetsAsync("ord-1", "f1");

        var act = () => NewOrders().AppendActionAsync("ord-1", new OrderActionLog(
            OrderActionType.Complete, PerformedBy: "u1", PerformedAt: ClosedAt, ResultingStatus: ServiceOrderStatus.Completed));

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
        (await ReleasedAtByTargetAsync())["ord-1:f1"].Should().BeNull();
    }

    // ── InspectionCommitService (transitions through UpdateAsync inside its own transaction) ──

    [Fact]
    public async Task InspectionCommit_MovingTheOrderToItsSuccessState_ReleasesItsTargets()
    {
        FieldWorkOrderTypes.RegisterAll(_registry);
        var rowVersion = await SeedCompletedFieldWorkOrderAsync("ord-field");
        await AddTargetsAsync("ord-field", "f1", "f2");
        _clock.SetUtcNow(ClosedAtInstant);

        await BuildInspectionCommitService().CommitAsync("ord-field", rowVersion, performedByUserId: "inspector-1", catalogInstanceId: "catalog-primary");

        (await LoadAsync("ord-field")).Status.Should().Be(FieldWorkStatus.Inspected);
        (await ReleasedAtByTargetAsync()).Values.Should().HaveCount(2).And.OnlyContain(stamp => stamp == ClosedAt);
    }

    [Fact]
    public async Task InspectionCommit_RolledBackOnAStaleRowVersion_LeavesTheOrdersTargetsUnreleased()
    {
        FieldWorkOrderTypes.RegisterAll(_registry);
        await SeedCompletedFieldWorkOrderAsync("ord-field");
        await AddTargetsAsync("ord-field", "f1");
        _clock.SetUtcNow(ClosedAtInstant);

        var act = () => BuildInspectionCommitService().CommitAsync(
            "ord-field", [1, 2, 3, 4, 5, 6, 7, 8], performedByUserId: "inspector-1", catalogInstanceId: "catalog-primary");

        await act.Should().ThrowAsync<ServiceOrderConcurrencyException>();
        (await ReleasedAtByTargetAsync())["ord-field:f1"].Should().BeNull();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Task SeedOrderWithTargetsAsync(string orderId, params string[] featureIds) =>
        SeedTypedOrderWithTargetsAsync(orderId, "inspection", ServiceOrderStatus.Draft, featureIds);

    private async Task SeedTypedOrderWithTargetsAsync(string orderId, string orderTypeId, string status, params string[] featureIds)
    {
        await NewOrders().AddAsync(new ServiceOrder
        {
            Id = orderId,
            Title = orderId,
            OrderTypeId = orderTypeId,
            Status = status,
            CreatedBy = "u1",
            CreatedAt = AddedAt.UtcDateTime,
        });
        await AddTargetsAsync(orderId, featureIds);
    }

    private Task<int> AddTargetsAsync(string orderId, params string[] featureIds) =>
        _targets.AddRangeAsync(
            orderId,
            featureIds
                .Select(featureId => new ServiceOrderTargetBuilder()
                    .WithId($"{orderId}:{featureId}")
                    .WithOrderId(orderId)
                    .WithFeatureId(featureId)
                    .WithOrganizationId(Guid.Empty)
                    .Build())
                .ToList(),
            CancellationToken.None);

    /// <summary>Reads through a fresh context, like a later request would — see <c>InspectionCommitServiceTests</c> on why.</summary>
    private async Task<ServiceOrder> LoadAsync(string orderId)
    {
        using var context = _fixture.NewContext();
        return (ServiceOrder)(await new EFServiceOrderRepository(context, orderTypeRegistry: _registry).GetByIdAsync(orderId))!;
    }

    private async Task UpdateStatusAsync(string orderId, string status)
    {
        var order = await LoadAsync(orderId);
        order.Status = status;
        await NewOrders().UpdateAsync(order);
    }

    /// <summary>What is actually committed, keyed by target id.</summary>
    private async Task<Dictionary<string, DateTime?>> ReleasedAtByTargetAsync()
    {
        using var context = _fixture.NewContext();
        return await context.ServiceOrderTargets.IgnoreQueryFilters().AsNoTracking()
            .ToDictionaryAsync(t => t.Id, t => t.ReleasedAt, StringComparer.Ordinal);
    }

    private async Task<byte[]> SeedCompletedFieldWorkOrderAsync(string orderId)
    {
        await NewOrders().AddAsync(new ServiceOrder
        {
            Id = orderId,
            OrderTypeId = FieldWorkOrderTypes.FieldWorkId,
            Status = FieldWorkStatus.Completed,
            CreatedBy = "tech-1",
            CreatedAt = AddedAt.UtcDateTime,
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ServiceOrderBoqAttributes.ContractorOrganizationIdKey] = Guid.Empty.ToString(),
                [ServiceOrderBoqAttributes.BoqLinesKey] = "[]",
            },
        });
        return (await LoadAsync(orderId)).RowVersion;
    }

    private InspectionCommitService BuildInspectionCommitService() => new(
        _fixture.Context,
        new ValidatingServiceOrderRepository(NewOrders(), _registry),
        new EFContractorUsPostingRepository(_fixture.Context),
        new EFUsCatalogRepository(_fixture.Context),
        new EFPavingMeasurementRepository(_fixture.Context),
        new EFOutboxStore(_fixture.Context, _clock, new ProtobufIntegrationMessageSerializer(), "test-service"),
        _registry,
        _clock);
}
