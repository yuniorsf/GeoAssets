using GeoAssets.NeutralOperator.Application.Occupancy.Saga;
using GeoAssets.NeutralOperator.Application;
using FluentAssertions;
using GeoAssets.NeutralOperator.Domain.Occupancy;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Services;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using GeoAssets.Workflow.Tests.Orders;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Workflow.Tests.Occupancy;

/// <summary>
/// <see cref="OccupancyCommitService"/> (XD01-318) beyond the shared contract: argument guards,
/// who is refused before any store is read, order types it does not handle, a set that stops
/// being staged under the approval, the cancellation exit before the first write, the order of
/// the writes, and its registration.
/// </summary>
public sealed class OccupancyCommitServiceTests
{
    private const string Order = "ord-1";
    private const string PoleType = "pole";

    private static readonly Guid Owner = OccupancyCommitFixture.Owner;
    private static readonly Guid Occupant = new("0c0c0c0c-0000-4000-8000-000000000318");

    private static readonly WorkflowPrincipal Supervisor = new("approver-1", Owner.ToString(), ["Supervisor"], [], []);

    private readonly OccupancyCommitFixture _fixture = new();

    // ── Arguments ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void Constructor_NullCollaborator_ThrowsArgumentNullException(int nullAt)
    {
        var f = _fixture;
        object?[] args = [f.Orders, f.OrderTypes, f.Rules, f.Targets, f.Occupancy, f.Occupancy, f.ChangeSets, f.ChangeSets, f.Outbox, f.UnitOfWork, f.Options, f.Clock, f.Sagas];
        args[nullAt] = null;

        var act = () => new OccupancyCommitService(
            (IServiceOrderRepository)args[0]!, (OrderTypeRegistry)args[1]!, (ServiceOrderRules)args[2]!, (IServiceOrderTargetReader)args[3]!,
            (IPoleOccupancyReader)args[4]!, (IPoleOccupancyWriter)args[5]!, (IOccupancyChangeSetReader)args[6]!, (IOccupancyChangeSetWriter)args[7]!,
            (IOutboxWriter)args[8]!, (IOccupancyApplySagaStore)args[12]!, (IWorkflowUnitOfWork)args[9]!, (OrderOccupancyOptions)args[10]!, (TimeProvider)args[11]!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public Task ApproveAndCommitAsync_BlankOrderId_ThrowsArgumentException(string? blank)
    {
        var act = () => _fixture.Sut.ApproveAndCommitAsync(blank!, Supervisor, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public Task ApproveAndCommitAsync_NullPrincipal_ThrowsArgumentNullException()
    {
        var act = () => _fixture.Sut.ApproveAndCommitAsync(Order, null!, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void AssetMutationBatchId_BlankOrderId_ThrowsArgumentException(string? blank)
    {
        var act = () => OccupancyCommitService.AssetMutationBatchId(blank!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AssetMutationBatchId_IsTheSameForTheSameOrder_AndDiffersBetweenOrders()
    {
        var first = OccupancyCommitService.AssetMutationBatchId("ord-1");

        first.Should().NotBe(Guid.Empty);
        OccupancyCommitService.AssetMutationBatchId("ord-1").Should().Be(first);
        OccupancyCommitService.AssetMutationBatchId("ord-2").Should().NotBe(first);
        OccupancyCommitService.CausationId("ord-1").Should().Be("occupancy-commit:ord-1");
    }

    // ── Callers refused before any store is read (owner only) ────────────────

    [Fact]
    public async Task ApproveAndCommitAsync_ByAGranteeTheRulesAllowToApprove_IsRefusedAsOwnerOnly_BeforeAnyStoreIsRead()
    {
        await SeedAsync();
        var grantee = new WorkflowPrincipal("grantee-1", Occupant.ToString(), ["Administrator"], [], [])
        {
            OrgGrants = [new WorkflowOrgGrant(Owner.ToString(), "ServiceOrder", ["serviceorders:view", "serviceorders:approve"], RequiredRole: null)],
        };
        _fixture.Rules.Evaluate(grantee, OrderActionType.Approve, (await _fixture.Orders.GetByIdAsync(Order))!).Allowed
            .Should().BeTrue("the grant is live: the rules alone would let the grantee approve");
        var untouchable = new UntouchableStores();
        var sut = _fixture.Build(targets: untouchable, occupancy: untouchable, occupancyWriter: untouchable,
            changeSets: untouchable, changeSetWriter: untouchable);

        var act = () => sut.ApproveAndCommitAsync(Order, grantee, CancellationToken.None);

        var denied = (await act.Should().ThrowAsync<OrderActionDeniedException>()).Which;
        denied.Evaluation!.Reason.Should().Be(OccupancyCommitService.OwnerOnlyReason);
        denied.Evaluation.IsOutsideOrganization.Should().BeFalse("the grantee is inside the gate; committing occupancy is the owner's alone");
        untouchable.Calls.Should().Be(0);
        (await _fixture.Orders.GetByIdAsync(Order))!.Status.Should().Be(ServiceOrderStatus.Completed);
    }

    [Fact]
    public async Task ApproveAndCommitAsync_ByTheOrdersDispatchRecipient_IsDenied_AndCommitsNothing()
    {
        await SeedAsync();
        await _fixture.Orders.AppendDispatchAsync(
            Order, new OrderDispatch(Occupant.ToString("D"), DispatchTargetType.Organization, "approver-1", OccupancyCommitFixture.Now.UtcDateTime));
        var recipient = new WorkflowPrincipal("occupant-supervisor", Occupant.ToString(), ["Supervisor"], [], []);

        var act = () => _fixture.Sut.ApproveAndCommitAsync(Order, recipient, CancellationToken.None);

        (await act.Should().ThrowAsync<OrderActionDeniedException>()).Which.Evaluation!.Reason
            .Should().Be(ServiceOrderRules.RecipientOrderIsReadOnlyReason, "XD01-333: the pole-work type only informs a recipient, which the rules refuse before the owner-only check");
        _fixture.UnitOfWork.Started.Should().Be(0);
    }

    [Fact]
    public async Task ApproveAndCommitAsync_OnAnOrderOfNoOrganization_ByAPrincipalOfNone_IsRefusedAsOwnerOnly()
    {
        SharedUseOrderTypes.RegisterAll(_fixture.OrderTypes);
        var orderless = NewOrder(SharedUseOrderTypes.PoleWorkId, ServiceOrderStatus.Completed, organizationId: Guid.Empty);
        await _fixture.Orders.AddAsync(orderless);
        var noOrganization = new WorkflowPrincipal("approver-x", null, ["Administrator"], [], []);
        _fixture.Rules.Evaluate(noOrganization, OrderActionType.Approve, orderless).Allowed
            .Should().BeTrue("an order of no organization passes the gate by default");

        var act = () => _fixture.Sut.ApproveAndCommitAsync(Order, noOrganization, CancellationToken.None);

        (await act.Should().ThrowAsync<OrderActionDeniedException>()).Which.Evaluation!.Reason.Should().Be(OccupancyCommitService.OwnerOnlyReason);
    }

    // ── Order types it does not handle ────────────────────────────────────────

    [Fact]
    public async Task ApproveAndCommitAsync_OrderOfAnUnregisteredType_ThrowsOrderTypeNotRegistered_NamingTheType()
    {
        await _fixture.Orders.AddAsync(NewOrder("ghost-type", ServiceOrderStatus.Completed));

        var act = () => _fixture.Sut.ApproveAndCommitAsync(Order, Supervisor, CancellationToken.None);

        (await act.Should().ThrowAsync<OrderTypeNotRegisteredException>()).Which.OrderTypeId.Should().Be("ghost-type");
    }

    [Fact]
    public async Task ApproveAndCommitAsync_OrderOfATypeWithoutTargetPolicy_ThrowsInvalidOperationException()
    {
        _fixture.OrderTypes.Register(new OrderType
        {
            Id = "plain-review",
            DisplayName = "Plain review",
            States = [new(ServiceOrderStatus.Completed, "Completed"), new("Approved", "Approved", IsSuccess: true)],
            Transitions = [new(ServiceOrderStatus.Completed, "Approved", OrderActionType.Approve)],
        });
        await _fixture.Orders.AddAsync(NewOrder("plain-review", ServiceOrderStatus.Completed));

        var act = () => _fixture.Sut.ApproveAndCommitAsync(Order, Supervisor, CancellationToken.None);

        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Should().NotBeOfType<InvalidServiceOrderTransitionException>();
        (await _fixture.Orders.GetByIdAsync(Order))!.Status.Should().Be(ServiceOrderStatus.Completed);
    }

    [Fact]
    public async Task ApproveAndCommitAsync_TypeWithATargetPolicyButNoGraphOfItsOwn_HasNoApproval_ThrowsInvalidServiceOrderTransitionException()
    {
        _fixture.OrderTypes.Register(new OrderType
        {
            Id = "pole-survey",
            DisplayName = "Pole survey",
            Capabilities = [OccupancyCapabilities.Commit],
            TargetPolicy = new OrderTargetPolicy { TargetAssetTypeIds = [PoleType] },
        });
        await _fixture.Orders.AddAsync(NewOrder("pole-survey", ServiceOrderStatus.Completed));

        var act = () => _fixture.Sut.ApproveAndCommitAsync(Order, Supervisor, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidServiceOrderTransitionException>();
    }

    [Fact]
    public async Task ApproveAndCommitAsync_AnOrderThatIsNotAServiceOrder_ThrowsInvalidOperationException_BeforeAnyWrite()
    {
        await SeedAsync();
        var orders = new RecordingOrders(_fixture.Orders) { Wrap = true };

        var act = () => _fixture.Build(orders: orders).ApproveAndCommitAsync(Order, Supervisor, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        orders.Writes.Should().BeEmpty();
        _fixture.UnitOfWork.Started.Should().Be(0);
    }

    // ── Under the approval ────────────────────────────────────────────────────

    [Fact]
    public async Task ApproveAndCommitAsync_ASetNoLongerStagedWhenTheSagaMovesItToPending_ThrowsOccupancyConflictException_NamingItsPole()
    {
        await SeedAsync();
        await StageAsync("pole-1", OccupancyChangeBuilder.Add(1));
        var sut = _fixture.Build(sagas: new LosingSagaStore(_fixture.Sagas));

        var act = () => sut.ApproveAndCommitAsync(Order, Supervisor, CancellationToken.None);

        (await act.Should().ThrowAsync<OccupancyConflictException>()).Which.PoleFeatureIds.Should().Equal("pole-1");
        _fixture.UnitOfWork.Completed.Should().Be(0);
        _fixture.Outbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveAndCommitAsync_CancelledAfterTheChecks_ThrowsBeforeTheFirstWrite()
    {
        await SeedAsync();
        await StageAsync("pole-1", OccupancyChangeBuilder.Add(1));
        using var cts = new CancellationTokenSource();
        var sut = _fixture.Build(changeSets: new CancellingChangeSetReader(_fixture.ChangeSets, cts));

        var act = () => sut.ApproveAndCommitAsync(Order, Supervisor, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _fixture.UnitOfWork.Started.Should().Be(0);
        (await _fixture.ChangeSets.GetStagedAsync(Order, OrderTargetSelectionService.TargetId(Order, "pole-1"), CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task ApproveAndCommitAsync_ALostRaceOnTheOrder_Surfaces_AndStagesNoBatch()
    {
        await SeedAsync();
        await StageAsync("pole-1", OccupancyChangeBuilder.Add(1));
        var orders = new RecordingOrders(_fixture.Orders) { FailUpdateWith = new ServiceOrderConcurrencyException(Order) };

        var act = () => _fixture.Build(orders: orders).ApproveAndCommitAsync(Order, Supervisor, CancellationToken.None);

        await act.Should().ThrowAsync<ServiceOrderConcurrencyException>();
        _fixture.Outbox.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task ApproveAndCommitAsync_MovesTheSetsToPendingBeforeUpdatingTheOrder_CarryingTheRowVersionItWasReadWith_ThenLogs()
    {
        await SeedAsync(rowVersion: [4, 2]);
        await StageAsync("pole-1", OccupancyChangeBuilder.Add(1));
        var setsAtUpdate = new List<OccupancyChangeSetStatus>();
        var orders = new RecordingOrders(_fixture.Orders)
        {
            BeforeUpdateAsync = async () => setsAtUpdate.AddRange(
                (await _fixture.ChangeSets.GetPageAsync(new OccupancyChangeSetQuery(Order), CancellationToken.None)).Items.Select(s => s.Status)),
        };

        await _fixture.Build(orders: orders).ApproveAndCommitAsync(Order, Supervisor, CancellationToken.None);

        setsAtUpdate.Should().Equal([OccupancyChangeSetStatus.Pending], "the set waited for the catalog before the order was marked awaiting apply");
        orders.Writes.Should().Equal($"update:{Order}", $"action:{Order}");
        orders.UpdatedRowVersions.Should().ContainSingle().Which.Should().Equal(4, 2);
    }

    [Fact]
    public async Task ApproveAndCommitAsync_CarriesTheOrdersScope_AndActorKind_OntoTheBatchAndTheLog()
    {
        await SeedAsync(scopeId: "scope-7");
        await StageAsync("pole-1", OccupancyChangeBuilder.Add(1));
        var agent = Supervisor with { Kind = ActorKind.Agent };

        await _fixture.Sut.ApproveAndCommitAsync(Order, agent, CancellationToken.None);

        ((AssetMutationBatchRequestedEvent)_fixture.Outbox.Messages.Single().Message).ScopeId.Should().Be("scope-7");
        (await _fixture.Orders.GetByIdAsync(Order))!.ActionLog.Single().ActorKind.Should().Be(ActorKind.Agent);
    }

    [Fact]
    public async Task ApproveAndCommitAsync_ChangesOfSeveralKinds_AreCountedPerKind()
    {
        await SeedAsync();
        var existing = new PoleOccupancyEntryBuilder().ForPole("pole-1").OwnedBy(Owner).OnSlot(1).OccupiedBy(Occupant).Build();
        var other = new PoleOccupancyEntryBuilder().ForPole("pole-1").OwnedBy(Owner).OnSlot(3).OccupiedBy(Occupant).Build();
        await _fixture.Occupancy.ReplaceEntriesAsync("pole-1", PoleOccupancy.UnsavedVersion, [existing, other], CancellationToken.None);
        await StageAsync("pole-1", OccupancyChangeBuilder.Add(2), OccupancyChangeBuilder.Add(4), OccupancyChangeBuilder.MarkSlotUnidentified(1), OccupancyChangeBuilder.RemoveSlot(3));

        var result = await _fixture.Sut.ApproveAndCommitAsync(Order, Supervisor, CancellationToken.None);

        result.ChangesByKind.Should().Equal(new Dictionary<OccupancyChangeKind, int>
        {
            [OccupancyChangeKind.Add] = 2,
            [OccupancyChangeKind.Remove] = 1,
            [OccupancyChangeKind.MarkUnidentified] = 1,
        });
        result.ChangesCommitted.Should().Be(4);
    }

    // ── Registration ──────────────────────────────────────────────────────────

    [Fact]
    public void AddOccupancyCommit_RegistersOneScopedService_ReadingTheHostsOptions()
    {
        var services = CommitServices();
        services.AddOrderOccupancyStaging(options => options.CatalogInstanceId = "catalog-9");
        services.AddOccupancyCommit();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var port = scope.ServiceProvider.GetRequiredService<IOccupancyCommitService>();

        port.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<OccupancyCommitService>());
        services.Single(d => d.ServiceType == typeof(IOccupancyCommitService)).Lifetime.Should().Be(ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddOccupancyCommit_WithoutOptionsOrClock_UsesTheDefaults()
    {
        var services = CommitServices();
        services.AddOccupancyCommit();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IOccupancyCommitService>().Should().BeOfType<OccupancyCommitService>();
    }

    [Fact]
    public void AddOccupancyCommit_RegistersTheApprovalSaga_AndItsTransitionGuard_ScopedAndOnce()
    {
        var services = CommitServices();
        services.AddOccupancyCommit();
        services.AddOccupancyCommit();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var saga = scope.ServiceProvider.GetRequiredService<IOccupancyApplySagaService>();

        saga.Should().BeSameAs(scope.ServiceProvider.GetRequiredService<OccupancyApplySagaService>());
        services.Single(d => d.ServiceType == typeof(IOccupancyApplySagaService)).Lifetime.Should().Be(ServiceLifetime.Scoped);
        scope.ServiceProvider.GetServices<ITransitionGuard>().OfType<OccupancyApplyPendingTransitionGuard>()
            .Should().ContainSingle("registering twice adds the guard once");
    }

    [Fact]
    public void AddOccupancyCommit_NullServices_ThrowsArgumentNullException()
    {
        var act = () => NeutralOperatorServiceExtensions.AddOccupancyCommit(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ServiceCollection CommitServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IServiceOrderRepository>(_fixture.Orders);
        services.AddSingleton(_fixture.OrderTypes);
        services.AddSingleton(_fixture.Rules);
        services.AddSingleton<IServiceOrderTargetReader>(_fixture.Targets);
        services.AddSingleton<IPoleOccupancyReader>(_fixture.Occupancy);
        services.AddSingleton<IPoleOccupancyWriter>(_fixture.Occupancy);
        services.AddSingleton<IOccupancyChangeSetReader>(_fixture.ChangeSets);
        services.AddSingleton<IOccupancyChangeSetWriter>(_fixture.ChangeSets);
        services.AddSingleton<IOutboxWriter>(_fixture.Outbox);
        services.AddSingleton<IWorkflowUnitOfWork>(_fixture.UnitOfWork);
        services.AddSingleton<IOccupancyApplySagaStore>(_fixture.Sagas);
        services.AddSingleton<IIdempotentInbox>(_fixture.Inbox);
        services.AddScoped<ITenantOrganizationScope, TenantOrganizationScope>();
        return services;
    }

    private static ServiceOrder NewOrder(
        string orderTypeId, string status, byte[]? rowVersion = null, string? scopeId = null, Guid? organizationId = null) => new()
    {
        Id = Order,
        Title = "Pole work",
        OrderTypeId = orderTypeId,
        Status = status,
        CreatedBy = "planner-1",
        OrganizationId = organizationId ?? Owner,
        CreatedAt = OccupancyCommitFixture.Now.UtcDateTime,
        RowVersion = rowVersion ?? [],
        Attributes = scopeId is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal) { ["scopeId"] = scopeId },
    };

    /// <summary>A completed pole-work order of the owner with one pole target.</summary>
    private async Task SeedAsync(byte[]? rowVersion = null, string? scopeId = null)
    {
        SharedUseOrderTypes.RegisterAll(_fixture.OrderTypes);
        await _fixture.Orders.AddAsync(NewOrder(SharedUseOrderTypes.PoleWorkId, ServiceOrderStatus.Completed, rowVersion, scopeId));
        await _fixture.Targets.AddRangeAsync(
            Order,
            [new ServiceOrderTargetBuilder().WithId(OrderTargetSelectionService.TargetId(Order, "pole-1")).WithOrderId(Order)
                .WithFeatureId("pole-1").WithAssetTypeId(PoleType).WithOrganizationId(Owner).Build()],
            CancellationToken.None);
    }

    private async Task StageAsync(string pole, params OccupancyChange[] changes)
    {
        var baseline = await _fixture.Occupancy.GetForPoleAsync(pole, CancellationToken.None);
        await _fixture.ChangeSets.StageAsync(
            OccupancyChangeSet.Stage(Order, OrderTargetSelectionService.TargetId(Order, pole), pole, Owner, changes, baseline, "crew-1", _fixture.Clock.GetUtcNow()),
            CancellationToken.None);
    }

    /// <summary>The saga store, except that every set it is asked to move to pending has just been replaced by someone else.</summary>
    private sealed class LosingSagaStore(IOccupancyApplySagaStore inner) : IOccupancyApplySagaStore
    {
        public Task<bool> BeginAsync(
            OccupancyApplySaga saga, IReadOnlyCollection<OccupancyApplySagaPole> poles, IReadOnlyCollection<Guid> changeSetIds, CancellationToken ct) =>
            Task.FromResult(false);

        public Task<OccupancyApplySagaCompletion> CompleteAsync(OccupancyApplySaga completed, CancellationToken ct) => inner.CompleteAsync(completed, ct);
        public Task<bool> RecordRetryAsync(Guid batchId, DateTimeOffset retriedAt, CancellationToken ct) => inner.RecordRetryAsync(batchId, retriedAt, ct);
        public Task<OccupancyApplySaga?> FindAsync(Guid batchId, CancellationToken ct) => inner.FindAsync(batchId, ct);
        public Task<OccupancyApplySaga?> FindLatestForOrderAsync(string orderId, CancellationToken ct) => inner.FindLatestForOrderAsync(orderId, ct);
        public Task<IReadOnlyList<string>> FindPendingPolesAsync(IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct) => inner.FindPendingPolesAsync(poleFeatureIds, ct);
        public Task<IReadOnlyList<OccupancyApplySagaPole>> GetPolesAsync(Guid batchId, CancellationToken ct) => inner.GetPolesAsync(batchId, ct);
    }

    /// <summary>The change-set store's read side, cancelling the caller's token as it answers — the caller gives up mid-approval.</summary>
    private sealed class CancellingChangeSetReader(IOccupancyChangeSetReader inner, CancellationTokenSource cts) : IOccupancyChangeSetReader
    {
        public Task<OccupancyChangeSet?> GetStagedAsync(string orderId, string targetId, CancellationToken ct) => inner.GetStagedAsync(orderId, targetId, ct);

        public async Task<PagedResult<OccupancyChangeSet>> GetPageAsync(OccupancyChangeSetQuery query, CancellationToken ct)
        {
            var page = await inner.GetPageAsync(query, ct);
            await cts.CancelAsync();
            return page;
        }
    }

    /// <summary>Every store port the approval reads or writes, counting any call: proof that a refusal came before any store was touched.</summary>
    private sealed class UntouchableStores : IServiceOrderTargetReader, IPoleOccupancyReader, IPoleOccupancyWriter, IOccupancyChangeSetReader, IOccupancyChangeSetWriter
    {
        public int Calls { get; private set; }

        private InvalidOperationException Touched()
        {
            Calls++;
            return new InvalidOperationException("A store was touched before the caller was authorized.");
        }

        public Task<PagedResult<ServiceOrderTarget>> GetPageAsync(OrderTargetQuery query, CancellationToken ct) => throw Touched();
        public Task<ServiceOrderTarget?> GetByIdAsync(string orderId, string targetId, CancellationToken ct) => throw Touched();
        public Task<int> CountAsync(string orderId, CancellationToken ct) => throw Touched();
        public Task<IReadOnlyList<OrderTargetUsage>> FindOpenUsagesAsync(IReadOnlyCollection<string> featureIds, string excludingOrderId, CancellationToken ct) => throw Touched();
        public Task<OrderTargetExtent?> GetExtentAsync(string orderId, CancellationToken ct) => throw Touched();
        public Task<PoleOccupancy?> GetForPoleAsync(string poleFeatureId, CancellationToken ct) => throw Touched();
        public Task<IReadOnlyList<PoleOccupancy>> GetForPolesAsync(IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct) => throw Touched();
        public Task<IReadOnlyList<PoleOccupantPresence>> FindContractedOccupantsAsync(IReadOnlyCollection<string> poleFeatureIds, CancellationToken ct) => throw Touched();
        public Task<PagedResult<PoleOccupancy>> GetPageAsync(PoleOccupancyQuery query, CancellationToken ct) => throw Touched();
        public Task<PoleOccupancy> ReplaceEntriesAsync(string poleFeatureId, int expectedVersion, IReadOnlyCollection<PoleOccupancyEntry> entries, CancellationToken ct) => throw Touched();
        public Task<OccupancyChangeSet?> GetStagedAsync(string orderId, string targetId, CancellationToken ct) => throw Touched();
        public Task<PagedResult<OccupancyChangeSet>> GetPageAsync(OccupancyChangeSetQuery query, CancellationToken ct) => throw Touched();
        public Task<OccupancyChangeSet> StageAsync(OccupancyChangeSet changeSet, CancellationToken ct) => throw Touched();
        public Task<bool> DiscardAsync(string orderId, string targetId, CancellationToken ct) => throw Touched();
        public Task<int> DiscardAllAsync(string orderId, CancellationToken ct) => throw Touched();
        public Task<bool> MarkCommittedAsync(OccupancyChangeSet committed, CancellationToken ct) => throw Touched();
    }

    /// <summary>
    /// The fake order store, recording its writes in order — and able to fail the update, to do
    /// something just before it, or to hand orders out as something other than <see cref="ServiceOrder"/>.
    /// </summary>
    private sealed class RecordingOrders(FakeServiceOrderRepository inner) : IServiceOrderRepository
    {
        public List<string> Writes { get; } = [];

        public List<byte[]> UpdatedRowVersions { get; } = [];

        public Exception? FailUpdateWith { get; init; }

        public Func<Task>? BeforeUpdateAsync { get; init; }

        public bool Wrap { get; init; }

        public event EventHandler<IServiceOrder>? OrderAdded { add => inner.OrderAdded += value; remove => inner.OrderAdded -= value; }
        public event EventHandler<IServiceOrder>? OrderUpdated { add => inner.OrderUpdated += value; remove => inner.OrderUpdated -= value; }
        public event EventHandler<(IServiceOrder Order, string Previous)>? OrderStatusChanged { add => inner.OrderStatusChanged += value; remove => inner.OrderStatusChanged -= value; }
        public event EventHandler<string>? OrderDeleted { add => inner.OrderDeleted += value; remove => inner.OrderDeleted -= value; }

        public async Task<IServiceOrder?> GetByIdAsync(string id, CancellationToken ct = default)
        {
            var order = await inner.GetByIdAsync(id, ct);
            return Wrap && order is not null ? new ForeignOrder(order) : order;
        }

        public Task<IReadOnlyList<IServiceOrder>> GetAllAsync(CancellationToken ct = default) => inner.GetAllAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetRootsAsync(CancellationToken ct = default) => inner.GetRootsAsync(ct);
        public Task<IReadOnlyList<IServiceOrder>> GetChildrenAsync(string parentId, CancellationToken ct = default) => inner.GetChildrenAsync(parentId, ct);
        public Task<IServiceOrder?> GetParentAsync(string childId, CancellationToken ct = default) => inner.GetParentAsync(childId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByStatusAsync(string status, CancellationToken ct = default) => inner.GetByStatusAsync(status, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByAssigneeAsync(string userId, CancellationToken ct = default) => inner.GetByAssigneeAsync(userId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByCreatorAsync(string userId, CancellationToken ct = default) => inner.GetByCreatorAsync(userId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByOrderTypeAsync(string orderTypeId, CancellationToken ct = default) => inner.GetByOrderTypeAsync(orderTypeId, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetByDateRangeAsync(DateTime from, DateTime toExclusive, CancellationToken ct = default) => inner.GetByDateRangeAsync(from, toExclusive, ct);
        public Task<IReadOnlyList<IServiceOrder>> GetDispatchedToAsync(string targetId, DispatchTargetType targetType, CancellationToken ct = default) => inner.GetDispatchedToAsync(targetId, targetType, ct);

        public Task AddAsync(IServiceOrder order, CancellationToken ct = default) => inner.AddAsync(order, ct);

        public async Task UpdateAsync(IServiceOrder order, CancellationToken ct = default)
        {
            if (BeforeUpdateAsync is not null) await BeforeUpdateAsync();
            if (FailUpdateWith is not null) throw FailUpdateWith;

            Writes.Add($"update:{order.Id}");
            UpdatedRowVersions.Add(order.RowVersion);
            await inner.UpdateAsync(order, ct);
        }

        public Task AppendDispatchAsync(string orderId, OrderDispatch dispatch, CancellationToken ct = default) => inner.AppendDispatchAsync(orderId, dispatch, ct);

        public Task AppendActionAsync(string orderId, OrderActionLog entry, CancellationToken ct = default)
        {
            Writes.Add($"action:{orderId}");
            return inner.AppendActionAsync(orderId, entry, ct);
        }

        public Task DeleteAsync(string id, CancellationToken ct = default) => inner.DeleteAsync(id, ct);
    }

    /// <summary>An <see cref="IServiceOrder"/> that is not a <see cref="ServiceOrder"/>.</summary>
    private sealed class ForeignOrder(IServiceOrder inner) : IServiceOrder
    {
        public string Id => inner.Id;
        public string Title => inner.Title;
        public string Description => inner.Description;
        public string OrderTypeId => inner.OrderTypeId;
        public byte[] RowVersion => inner.RowVersion;
        public string Status => inner.Status;
        public ServiceOrderPriority Priority => inner.Priority;
        public string CreatedBy => inner.CreatedBy;
        public string? AssignedTo => inner.AssignedTo;
        public DateTime CreatedAt => inner.CreatedAt;
        public DateTime? UpdatedAt => inner.UpdatedAt;
        public DateTime? ScheduledAt => inner.ScheduledAt;
        public DateTime? CompletedAt => inner.CompletedAt;
        public IReadOnlyDictionary<string, string> Attributes => inner.Attributes;
        public IReadOnlyList<GeoFeature> Features => inner.Features;
        public GeoAssets.Workflow.Selection.FeatureSelectionSpec? SelectionSpec => inner.SelectionSpec;
        public string? ParentOrderId => inner.ParentOrderId;
        public IReadOnlyList<string> ChildOrderIds => inner.ChildOrderIds;
        public IReadOnlyList<OrderDispatch> Dispatches => inner.Dispatches;
        public IReadOnlyList<OrderActionLog> ActionLog => inner.ActionLog;
        public Guid OrganizationId => inner.OrganizationId;
    }
}
