using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Registry;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Targets;

/// <summary>
/// <see cref="TargetLedgerService"/> (XD01-379) over an in-memory ledger store, outbox and inbox: reservation,
/// release and commit with their integration events, cross-vertical exclusivity, the terminal-release consumer, and the
/// organization scope it enters before every store access.
/// </summary>
public sealed class TargetLedgerServiceTests
{
    private static readonly Guid OrgX = new("c0c0c0c0-0000-4000-8000-000000000379");
    private static readonly Guid OrgY = new("d0d0d0d0-0000-4000-8000-000000000379");
    private static readonly string[] CommittedIds = ["f1", "f2"];

    private readonly InMemoryTargetLedgerStore _store = new();
    private readonly InMemoryWorkflowUnitOfWork _unitOfWork = new();
    private readonly RecordingOutboxWriter _outbox = new();
    private readonly RecordingIdempotentInbox _inbox = new();
    private readonly FakeTimeProvider _clock = new(TargetLedgerServiceHarness.Now);
    private readonly TenantOrganizationScope _scope = new();
    private readonly InMemoryOrderTypeDescriptorRegistry _descriptors = new([]);

    private TargetLedgerService Service(params string[] featureIds) => TargetLedgerServiceHarness.Create(
        _store,
        _unitOfWork,
        _outbox,
        _inbox,
        _descriptors,
        [.. featureIds.Select(id => TargetLedgerServiceHarness.Feature(id, OrgX)), .. featureIds.Select(id => TargetLedgerServiceHarness.Feature(id + "-y", OrgY))],
        _clock,
        _scope);

    private static OrderTargetPolicy Policy(OrderTargetExclusivity exclusivity) =>
        new() { TargetAssetTypeIds = ["pole"], Exclusivity = exclusivity };

    private static TargetLedgerOrder Order(string vertical, string orderId, OrderTargetExclusivity exclusivity, Guid? organization = null) =>
        new(vertical, orderId, $"{vertical}-type", organization ?? OrgX, Policy(exclusivity));

    private static TargetSelectionRequest Ids(string orderId, params string[] featureIds) => TargetLedgerServiceHarness.ByIds(orderId, featureIds);

    private IReadOnlyList<StagedOutboxMessage> Staged => _outbox.Messages;

    // ── Reserve ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReserveAsync_StagesOneReservedEventWithTheInsertedTargets_InTheSameUnitOfWork()
    {
        var service = Service("f1", "f2");

        var result = await service.ReserveAsync(Order("isp", "o1", OrderTargetExclusivity.BlockOnOverlap), Ids("o1", "f1", "f2"), "alice", CancellationToken.None);

        result.Added.Should().Be(2);
        _store.Rows.Select(r => r.Target.FeatureId).Should().BeEquivalentTo("f1", "f2");
        _unitOfWork.Started.Should().Be(1);
        var message = Staged.Should().ContainSingle().Subject;
        message.MessageType.Should().Be(IntegrationEventTypes.TargetsReservedV1);
        message.PartitionKey.Should().Be("isp/o1");
        var reserved = message.Message.Should().BeOfType<TargetsReservedEvent>().Subject;
        reserved.VerticalId.Should().Be("isp");
        reserved.OrderId.Should().Be("o1");
        reserved.OrganizationId.Should().Be(OrgX);
        reserved.PerformedBy.Should().Be("alice");
        reserved.OccurredAt.Should().Be(TargetLedgerServiceHarness.Now);
        reserved.Targets.Should().BeEquivalentTo(
            [new TargetLedgerEventItem(TargetLedger.TargetId("isp", "o1", "f1"), "f1"), new TargetLedgerEventItem(TargetLedger.TargetId("isp", "o1", "f2"), "f2")]);
        _store.Rows.Should().OnlyContain(r => r.Target.AddedBy == "alice" && r.Target.VerticalId == "isp");
        _scope.OrganizationId.Should().Be(OrgX);
    }

    [Fact]
    public async Task ReserveAsync_SameVerticalRepeat_IsIdempotent_NoDuplicateRowsAddedZeroAndNoEvent()
    {
        var service = Service("f1", "f2");
        var order = Order("isp", "o1", OrderTargetExclusivity.BlockOnOverlap);
        await service.ReserveAsync(order, Ids("o1", "f1", "f2"), "alice", CancellationToken.None);

        var again = await service.ReserveAsync(order, Ids("o1", "f1", "f2"), "alice", CancellationToken.None);

        again.Added.Should().Be(0);
        again.AlreadyInOrder.Should().Be(2);
        _store.Rows.Should().HaveCount(2);
        Staged.Should().ContainSingle("a reservation that inserted nothing reports nothing");
    }

    [Fact]
    public async Task ReserveAsync_PartialOverlapWithOwnTargets_ReportsOnlyTheNewOnes()
    {
        var service = Service("f1", "f2");
        var order = Order("isp", "o1", OrderTargetExclusivity.None);
        await service.ReserveAsync(order, Ids("o1", "f1"), "alice", CancellationToken.None);

        var result = await service.ReserveAsync(order, Ids("o1", "f1", "f2"), "alice", CancellationToken.None);

        result.Added.Should().Be(1);
        ((TargetsReservedEvent)Staged[1].Message).Targets.Select(t => t.FeatureId).Should().Equal("f2");
    }

    [Fact]
    public async Task ReserveAsync_AnotherVerticalHoldsTheFeatureExclusively_ThrowsConflictAndWritesNothing()
    {
        var service = Service("f1");
        await service.ReserveAsync(Order("vertical-a", "a1", OrderTargetExclusivity.BlockOnOverlap), Ids("a1", "f1"), "alice", CancellationToken.None);
        _outbox.Messages.Count.Should().Be(1);

        var act = () => service.ReserveAsync(Order("vertical-b", "b1", OrderTargetExclusivity.BlockOnOverlap), Ids("b1", "f1"), "bob", CancellationToken.None);

        var conflict = (await act.Should().ThrowAsync<TargetLedgerConflictException>()).Which.Conflicts.Should().ContainSingle().Subject;
        conflict.FeatureId.Should().Be("f1");
        conflict.VerticalId.Should().Be("vertical-a");
        conflict.OrderId.Should().Be("a1", "the holder belongs to the reserving organization");
        conflict.OrderTypeId.Should().Be("vertical-a-type");
        conflict.Exclusivity.Should().Be(OrderTargetExclusivity.BlockOnOverlap);
        _store.Rows.Should().ContainSingle().Which.Target.VerticalId.Should().Be("vertical-a");
        Staged.Should().ContainSingle("no event is staged for the refused reservation");
        _unitOfWork.Started.Should().Be(1, "the refused reservation never opens a unit of work");
    }

    [Fact]
    public async Task ReserveAsync_AnotherOrganizationsExclusiveHolder_IsReportedWithoutItsOrder()
    {
        var holder = TargetLedgerServiceHarness.Create(
            _store, _unitOfWork, _outbox, _inbox, _descriptors, [TargetLedgerServiceHarness.Feature("shared", OrgY)], _clock, new TenantOrganizationScope());
        await holder.ReserveAsync(Order("vertical-a", "a1", OrderTargetExclusivity.BlockOnOverlap, OrgY), Ids("a1", "shared"), "carol", CancellationToken.None);
        var service = TargetLedgerServiceHarness.Create(
            _store, _unitOfWork, _outbox, _inbox, _descriptors, [TargetLedgerServiceHarness.Feature("shared", OrgX)], _clock, _scope);

        var act = () => service.ReserveAsync(Order("vertical-b", "b1", OrderTargetExclusivity.BlockOnOverlap), Ids("b1", "shared"), "bob", CancellationToken.None);

        var conflict = (await act.Should().ThrowAsync<TargetLedgerConflictException>()).Which.Conflicts.Should().ContainSingle().Subject;
        conflict.VerticalId.Should().Be("vertical-a");
        conflict.OrderId.Should().BeNull();
        conflict.OrderTypeId.Should().BeNull();
    }

    [Fact]
    public async Task ReserveAsync_SharedPolicies_DoNotBlock()
    {
        var service = Service("f1");
        await service.ReserveAsync(Order("vertical-a", "a1", OrderTargetExclusivity.None), Ids("a1", "f1"), "alice", CancellationToken.None);

        var result = await service.ReserveAsync(Order("vertical-b", "b1", OrderTargetExclusivity.None), Ids("b1", "f1"), "bob", CancellationToken.None);

        result.Added.Should().Be(1);
        result.WarningCount.Should().Be(0);
        _store.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task ReserveAsync_WarnPolicyOverlap_ReservesAndCountsTheWarning()
    {
        var service = Service("f1");
        await service.ReserveAsync(Order("vertical-a", "a1", OrderTargetExclusivity.WarnOnOverlap), Ids("a1", "f1"), "alice", CancellationToken.None);

        var result = await service.ReserveAsync(Order("vertical-b", "b1", OrderTargetExclusivity.None), Ids("b1", "f1"), "bob", CancellationToken.None);

        result.Added.Should().Be(1);
        result.WarningCount.Should().Be(1);
    }

    [Fact]
    public async Task ReserveAsync_OwnPolicyBlocksOverlapWithASharedHolder_ThrowsConflict()
    {
        var service = Service("f1");
        await service.ReserveAsync(Order("vertical-a", "a1", OrderTargetExclusivity.None), Ids("a1", "f1"), "alice", CancellationToken.None);

        var act = () => service.ReserveAsync(Order("vertical-b", "b1", OrderTargetExclusivity.BlockOnOverlap), Ids("b1", "f1"), "bob", CancellationToken.None);

        await act.Should().ThrowAsync<TargetLedgerConflictException>();
    }

    [Fact]
    public async Task ReserveAsync_ReleasedHolder_NoLongerBlocks()
    {
        var service = Service("f1");
        var holder = Order("vertical-a", "a1", OrderTargetExclusivity.BlockOnOverlap);
        await service.ReserveAsync(holder, Ids("a1", "f1"), "alice", CancellationToken.None);
        await service.ReleaseAsync(holder.Key, OrgX, null, "alice", CancellationToken.None);

        var result = await service.ReserveAsync(Order("vertical-b", "b1", OrderTargetExclusivity.BlockOnOverlap), Ids("b1", "f1"), "bob", CancellationToken.None);

        result.Added.Should().Be(1);
    }

    [Fact]
    public async Task ReserveAsync_CancelledBeforeTheWrite_LeavesTheLedgerUntouched()
    {
        var service = Service("f1");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var act = () => service.ReserveAsync(Order("isp", "o1", OrderTargetExclusivity.None), Ids("o1", "f1"), "alice", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _store.Rows.Should().BeEmpty();
        Staged.Should().BeEmpty();
    }

    [Fact]
    public async Task PreviewAsync_WritesNothingAndStagesNothing()
    {
        var service = Service("f1", "f2");

        var preview = await service.PreviewAsync(Order("isp", "o1", OrderTargetExclusivity.None), Ids("o1", "f1", "f2"), "alice", CancellationToken.None);

        preview.Matched.Should().Be(2);
        preview.FirstPage.TotalCount.Should().Be(2);
        _store.Rows.Should().BeEmpty();
        Staged.Should().BeEmpty();
        _unitOfWork.Started.Should().Be(0);
        _scope.OrganizationId.Should().Be(OrgX, "the preview reads the ledger, so it enters the organization too");
    }

    // ── Release and commit ────────────────────────────────────────────────────

    [Fact]
    public async Task ReleaseAsync_StagesOneReleasedEventWithTheReleasedTargets_AndRepeatChangesNothing()
    {
        var service = Service("f1", "f2");
        var order = Order("isp", "o1", OrderTargetExclusivity.None);
        await service.ReserveAsync(order, Ids("o1", "f1", "f2"), "alice", CancellationToken.None);

        var released = await service.ReleaseAsync(order.Key, OrgX, null, "alice", CancellationToken.None);
        var repeat = await service.ReleaseAsync(order.Key, OrgX, null, "alice", CancellationToken.None);

        released.Should().NotBeNull().And.HaveCount(2);
        repeat.Should().NotBeNull().And.BeEmpty();
        Staged.Should().HaveCount(2);
        var message = Staged[1];
        message.MessageType.Should().Be(IntegrationEventTypes.TargetsReleasedV1);
        message.PartitionKey.Should().Be("isp/o1");
        var evt = message.Message.Should().BeOfType<TargetsReleasedEvent>().Subject;
        evt.Targets.Select(t => t.FeatureId).Should().BeEquivalentTo("f1", "f2");
        evt.OccurredAt.Should().Be(TargetLedgerServiceHarness.Now);
        _store.Rows.Should().OnlyContain(r => r.ReleasedAt != null);
    }

    [Fact]
    public async Task ReleaseAsync_SubsetOfTargetIds_ReleasesOnlyThose()
    {
        var service = Service("f1", "f2");
        var order = Order("isp", "o1", OrderTargetExclusivity.None);
        await service.ReserveAsync(order, Ids("o1", "f1", "f2"), "alice", CancellationToken.None);

        var released = await service.ReleaseAsync(order.Key, OrgX, [TargetLedger.TargetId("isp", "o1", "f2")], null, CancellationToken.None);

        released.Should().ContainSingle().Which.FeatureId.Should().Be("f2");
        ((TargetsReleasedEvent)Staged[^1].Message).Targets.Should().ContainSingle();
        _store.Rows.Count(r => r.ReleasedAt == null).Should().Be(1);
    }

    [Fact]
    public async Task ReleaseAndCommit_UnknownOrderOrAnotherOrganizationsOrder_ReturnNull_AndWriteNothing()
    {
        var service = Service("f1");
        var order = Order("isp", "o1", OrderTargetExclusivity.None);
        await service.ReserveAsync(order, Ids("o1", "f1"), "alice", CancellationToken.None);
        var other = TargetLedgerServiceHarness.Create(_store, _unitOfWork, _outbox, _inbox, _descriptors, [], _clock, new TenantOrganizationScope());

        (await other.ReleaseAsync(order.Key, OrgY, null, null, CancellationToken.None)).Should().BeNull();
        (await other.CommitAsync(order.Key, OrgY, null, null, CancellationToken.None)).Should().BeNull();
        (await service.ReleaseAsync(new TargetLedgerKey("isp", "nope"), OrgX, null, null, CancellationToken.None)).Should().BeNull();

        Staged.Should().ContainSingle();
        _store.Rows.Should().OnlyContain(r => r.ReleasedAt == null);
    }

    [Fact]
    public async Task CommitAsync_MarksTargetsDone_StagesOneCommittedEvent_RepeatChangesNothing_AndTheTargetStillBlocks()
    {
        var service = Service("f1", "f2");
        var order = Order("vertical-a", "a1", OrderTargetExclusivity.BlockOnOverlap);
        await service.ReserveAsync(order, Ids("a1", "f1", "f2"), "alice", CancellationToken.None);

        var committed = await service.CommitAsync(order.Key, OrgX, null, "alice", CancellationToken.None);
        var repeat = await service.CommitAsync(order.Key, OrgX, null, "alice", CancellationToken.None);

        committed.Should().HaveCount(2).And.OnlyContain(t => t.Status == OrderTargetStatus.Done && t.Version == 1);
        repeat.Should().BeEmpty();
        var message = Staged.Should().HaveCount(2).And.Subject.Last();
        message.MessageType.Should().Be(IntegrationEventTypes.TargetsCommittedV1);
        message.PartitionKey.Should().Be("vertical-a/a1");
        message.Message.Should().BeOfType<TargetsCommittedEvent>().Which.Targets.Select(t => t.FeatureId).Should().BeEquivalentTo(CommittedIds);
        var act = () => service.ReserveAsync(Order("vertical-b", "b1", OrderTargetExclusivity.BlockOnOverlap), Ids("b1", "f1"), "bob", CancellationToken.None);
        await act.Should().ThrowAsync<TargetLedgerConflictException>("a committed target keeps holding its feature until released");
    }

    [Fact]
    public async Task GetPageAsync_ReturnsTheOrdersTargetsInTheOrganization_ReleasedOnesIncluded()
    {
        var service = Service("f1", "f2", "f3");
        var order = Order("isp", "o1", OrderTargetExclusivity.None);
        await service.ReserveAsync(order, Ids("o1", "f3", "f1", "f2"), "alice", CancellationToken.None);
        await service.ReleaseAsync(order.Key, OrgX, [TargetLedger.TargetId("isp", "o1", "f1")], null, CancellationToken.None);

        var page = await service.GetPageAsync(order.Key, OrgX, 1, 2, CancellationToken.None);

        page.TotalCount.Should().Be(3);
        page.Items.Select(e => e.Target.FeatureId).Should().Equal("f2", "f3");
        (await service.GetPageAsync(order.Key, OrgX, 0, 1, CancellationToken.None)).Items.Single().ReleasedAt.Should().NotBeNull();
        var other = TargetLedgerServiceHarness.Create(_store, _unitOfWork, _outbox, _inbox, _descriptors, [], _clock, new TenantOrganizationScope());
        (await other.GetPageAsync(order.Key, OrgY, 0, 10, CancellationToken.None)).TotalCount.Should().Be(0);
    }

    // ── Organization scope ────────────────────────────────────────────────────

    [Fact]
    public async Task EveryOperation_EntersTheOrganizationBeforeTouchingTheStore()
    {
        var service = Service("f1");
        var key = new TargetLedgerKey("isp", "o1");

        (await service.BelongsToAnotherOrganizationAsync(key, OrgX, CancellationToken.None)).Should().BeFalse();
        _scope.OrganizationId.Should().Be(OrgX);
        // A different organization on the same unit of work is refused before any store call.
        var calls = _store.Calls;
        var act = () => service.GetPageAsync(key, OrgY, 0, 10, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _store.Calls.Should().Be(calls);
    }

    [Fact]
    public async Task BelongsToAnotherOrganizationAsync_TrueOnlyWhenTheOrderHoldsAnotherOrganizationsTargets()
    {
        var service = Service("f1");
        var order = Order("isp", "o1", OrderTargetExclusivity.None);
        await service.ReserveAsync(order, Ids("o1", "f1"), "alice", CancellationToken.None);
        var other = TargetLedgerServiceHarness.Create(_store, _unitOfWork, _outbox, _inbox, _descriptors, [], _clock, new TenantOrganizationScope());

        (await service.BelongsToAnotherOrganizationAsync(order.Key, OrgX, CancellationToken.None)).Should().BeFalse();
        (await other.BelongsToAnotherOrganizationAsync(order.Key, OrgY, CancellationToken.None)).Should().BeTrue();
        (await other.BelongsToAnotherOrganizationAsync(new TargetLedgerKey("isp", "unknown"), OrgY, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public Task Operations_EmptyOrganization_AreRejected()
    {
        var service = Service();

        var act = () => service.GetPageAsync(new TargetLedgerKey("isp", "o1"), Guid.Empty, 0, 10, CancellationToken.None);

        return act.Should().ThrowAsync<ArgumentException>();
    }

    // ── FindOrderType and construction ────────────────────────────────────────

    [Fact]
    public void FindOrderType_MatchesVerticalAndTypeOrdinally()
    {
        _descriptors.Upsert("isp", [new OrderTypeDescriptor { VerticalId = "isp", OrderTypeId = "survey", DisplayNameKey = "k", ApiBaseRoute = "/r" }]);
        var service = Service();

        service.FindOrderType("isp", "survey").Should().NotBeNull();
        service.FindOrderType("isp", "Survey").Should().BeNull();
        service.FindOrderType("other", "survey").Should().BeNull();
        var act = () => service.FindOrderType(null!, "survey");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullCollaborator_Throws()
    {
        var act = () => new TargetLedgerService(
            TargetLedgerServiceHarness.CreateSelection([], _clock), _store, _unitOfWork, _outbox, _inbox, _descriptors, _clock, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("organizationScope");
    }

    // ── Terminal release (AC2) ────────────────────────────────────────────────

    private static OrderStateChangedEvent StateChanged(
        string? vertical = "vertical-a", bool? isTerminal = true, Guid? organization = null, string newStatus = "Closed", string orderId = "a1") =>
        new(orderId, "vertical-a-type", "Open", newStatus, "operator", TargetLedgerServiceHarness.Now, VerticalId: vertical, IsTerminal: isTerminal, OrganizationId: organization ?? OrgX);

    private static IntegrationMessageContext Context(Guid messageId) =>
        new IntegrationMessageContextBuilder().WithMessageId(messageId).WithMessageType(IntegrationEventTypes.OrderStateChangedV1).Build();

    [Fact]
    public async Task ReserveAsync_AHolderCommittedBetweenThePlanAndTheLocks_IsCaughtByTheRecheck_AndNothingIsWritten()
    {
        var service = Service("f1");
        _store.OnLockFeatures = async ct =>
        {
            _store.OnLockFeatures = null;
            await Service("f1").ReserveAsync(Order("vertical-b", "b1", OrderTargetExclusivity.BlockOnOverlap), Ids("b1", "f1"), "bob", ct);
        };

        var act = () => service.ReserveAsync(Order("vertical-a", "a1", OrderTargetExclusivity.BlockOnOverlap), Ids("a1", "f1"), "alice", CancellationToken.None);

        (await act.Should().ThrowAsync<TargetLedgerConflictException>()).Which.Conflicts
            .Should().ContainSingle(c => c.FeatureId == "f1" && c.VerticalId == "vertical-b");
        (await service.GetPageAsync(new TargetLedgerKey("vertical-a", "a1"), OrgX, 0, 10, CancellationToken.None)).TotalCount.Should().Be(0);
    }

    private async Task<TargetLedgerService> HoldingServiceAsync()
    {
        var service = Service("f1", "f2");
        await service.ReserveAsync(Order("vertical-a", "a1", OrderTargetExclusivity.BlockOnOverlap), Ids("a1", "f1", "f2"), "alice", CancellationToken.None);
        return service;
    }

    [Fact]
    public async Task ReleaseTerminalAsync_TerminalEventHandledTwiceWithTheSameMessageId_ReleasesOnceAndStagesOneEvent()
    {
        var service = await HoldingServiceAsync();
        var messageId = Guid.NewGuid();

        var first = await service.ReleaseTerminalAsync(StateChanged(), Context(messageId), CancellationToken.None);
        var second = await service.ReleaseTerminalAsync(StateChanged(), Context(messageId), CancellationToken.None);

        first.Should().BeTrue();
        second.Should().BeFalse();
        _store.Rows.Should().OnlyContain(r => r.ReleasedAt == TargetLedgerServiceHarness.Now.UtcDateTime);
        Staged.Where(m => string.Equals(m.MessageType, IntegrationEventTypes.TargetsReleasedV1, StringComparison.Ordinal)).Should().ContainSingle();
        var evt = (TargetsReleasedEvent)Staged[^1].Message;
        evt.Targets.Select(t => t.FeatureId).Should().BeEquivalentTo("f1", "f2");
        evt.OrganizationId.Should().Be(OrgX);
        evt.PerformedBy.Should().Be("operator");
        _inbox.RecordedEntries.Should().ContainSingle().Which.Should().Be(
            (messageId, TargetLedgerService.TerminalReleaseConsumerName, IntegrationEventTypes.OrderStateChangedV1, TargetLedgerService.InboxRetention));
        _scope.OrganizationId.Should().Be(OrgX, "the event's organization is entered so row-level security sees the order's rows");
    }

    [Fact]
    public async Task ReleaseTerminalAsync_NewMessageIdAfterRelease_RecordsInboxButReleasesNothing()
    {
        var service = await HoldingServiceAsync();
        await service.ReleaseTerminalAsync(StateChanged(), Context(Guid.NewGuid()), CancellationToken.None);

        var again = await service.ReleaseTerminalAsync(StateChanged(), Context(Guid.NewGuid()), CancellationToken.None);

        again.Should().BeFalse("nothing was left to release");
        Staged.Count(m => string.Equals(m.MessageType, IntegrationEventTypes.TargetsReleasedV1, StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public async Task ReleaseTerminalAsync_IsTerminalNull_UsesTheDescriptorsTerminalStatuses()
    {
        _descriptors.Upsert("vertical-a", [new OrderTypeDescriptor
        {
            VerticalId = "vertical-a", OrderTypeId = "vertical-a-type", DisplayNameKey = "k", ApiBaseRoute = "/r", TerminalStatuses = ["Closed"],
        }]);
        var service = await HoldingServiceAsync();

        (await service.ReleaseTerminalAsync(StateChanged(isTerminal: null, newStatus: "Reviewing"), Context(Guid.NewGuid()), CancellationToken.None)).Should().BeFalse();
        _store.Rows.Should().OnlyContain(r => r.ReleasedAt == null);
        (await service.ReleaseTerminalAsync(StateChanged(isTerminal: null, newStatus: "Closed"), Context(Guid.NewGuid()), CancellationToken.None)).Should().BeTrue();

        _store.Rows.Should().OnlyContain(r => r.ReleasedAt != null);
    }

    [Fact]
    public async Task ReleaseTerminalAsync_IsTerminalNullAndNoDescriptor_ReleasesNothing()
    {
        var service = await HoldingServiceAsync();

        (await service.ReleaseTerminalAsync(StateChanged(isTerminal: null), Context(Guid.NewGuid()), CancellationToken.None)).Should().BeFalse();

        _store.Rows.Should().OnlyContain(r => r.ReleasedAt == null);
    }

    [Fact]
    public async Task ReleaseTerminalAsync_IsTerminalFalse_ReleasesNothing_AndDoesNotConsumeTheInbox()
    {
        var service = await HoldingServiceAsync();
        var messageId = Guid.NewGuid();

        (await service.ReleaseTerminalAsync(StateChanged(isTerminal: false), Context(messageId), CancellationToken.None)).Should().BeFalse();

        _store.Rows.Should().OnlyContain(r => r.ReleasedAt == null);
        _inbox.RecordedEntries.Should().BeEmpty("a non-terminal event is skipped before the inbox, so it can still be applied later");
        (await service.ReleaseTerminalAsync(StateChanged(isTerminal: true), Context(messageId), CancellationToken.None)).Should().BeTrue();
    }

    [Fact]
    public async Task ReleaseTerminalAsync_IsTerminalFalse_WinsOverADescriptorsTerminalStatus()
    {
        _descriptors.Upsert("vertical-a", [new OrderTypeDescriptor
        {
            VerticalId = "vertical-a", OrderTypeId = "vertical-a-type", DisplayNameKey = "k", ApiBaseRoute = "/r", TerminalStatuses = ["Closed"],
        }]);
        var service = await HoldingServiceAsync();

        (await service.ReleaseTerminalAsync(StateChanged(isTerminal: false, newStatus: "Closed"), Context(Guid.NewGuid()), CancellationToken.None)).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("kernel")]
    [InlineData("Not Well Formed")]
    [InlineData("")]
    public async Task ReleaseTerminalAsync_VerticalNullKernelOrMalformed_IsIgnored_WithoutTouchingStoreOrInbox(string? vertical)
    {
        var service = await HoldingServiceAsync();
        var calls = _store.Calls;
        var started = _unitOfWork.Started;

        (await service.ReleaseTerminalAsync(StateChanged(vertical), Context(Guid.NewGuid()), CancellationToken.None)).Should().BeFalse();

        _store.Calls.Should().Be(calls);
        _unitOfWork.Started.Should().Be(started);
        _inbox.RecordedEntries.Should().BeEmpty();
        _store.Rows.Should().OnlyContain(r => r.ReleasedAt == null);
    }

    [Fact]
    public async Task ReleaseTerminalAsync_BlankOrderId_IsIgnored()
    {
        var service = await HoldingServiceAsync();

        (await service.ReleaseTerminalAsync(StateChanged(orderId: " "), Context(Guid.NewGuid()), CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task ReleaseTerminalAsync_NoOrganizationOnTheEvent_ReleasesNothing_AndLeavesTheInboxUnconsumed()
    {
        var service = await HoldingServiceAsync();
        var scope = new TenantOrganizationScope();
        var unscoped = TargetLedgerServiceHarness.Create(_store, _unitOfWork, _outbox, _inbox, _descriptors, [], _clock, scope);
        var messageId = Guid.NewGuid();

        (await unscoped.ReleaseTerminalAsync(StateChanged() with { OrganizationId = null }, Context(messageId), CancellationToken.None)).Should().BeFalse();

        scope.OrganizationId.Should().BeNull();
        (await service.ReleaseTerminalAsync(StateChanged(), Context(messageId), CancellationToken.None))
            .Should().BeTrue("the message without an organization was not recorded in the inbox, so its corrected redelivery still applies");
    }

    [Fact]
    public async Task ReleaseTerminalAsync_KeyHeldInTwoOrganizations_ReleasesOnlyTheEventsOrganization()
    {
        var service = Service("f1");
        await service.ReserveAsync(Order("vertical-a", "a1", OrderTargetExclusivity.None), Ids("a1", "f1"), "alice", CancellationToken.None);
        var other = TargetLedgerServiceHarness.Create(
            _store, _unitOfWork, _outbox, _inbox, _descriptors, [TargetLedgerServiceHarness.Feature("f9", OrgY)], _clock, new TenantOrganizationScope());
        await other.ReserveAsync(Order("vertical-a", "a1", OrderTargetExclusivity.None, OrgY), Ids("a1", "f9"), "carol", CancellationToken.None);

        (await other.ReleaseTerminalAsync(StateChanged() with { OrganizationId = OrgY }, Context(Guid.NewGuid()), CancellationToken.None)).Should().BeTrue();

        Staged.OfType<StagedOutboxMessage>().Where(m => string.Equals(m.MessageType, IntegrationEventTypes.TargetsReleasedV1, StringComparison.Ordinal))
            .Select(m => ((TargetsReleasedEvent)m.Message).OrganizationId).Should().BeEquivalentTo([OrgY]);
    }

    [Fact]
    public async Task ReleaseTerminalAsync_NullArguments_Throw()
    {
        var service = Service();

        var message = () => service.ReleaseTerminalAsync(null!, Context(Guid.NewGuid()), CancellationToken.None);
        var context = () => service.ReleaseTerminalAsync(StateChanged(), null!, CancellationToken.None);

        await message.Should().ThrowAsync<ArgumentNullException>();
        await context.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Handler_DelegatesToReleaseTerminal()
    {
        var service = await HoldingServiceAsync();
        var handler = new TargetLedgerOrderStateChangedHandler(service);

        await handler.HandleAsync(StateChanged(), Context(Guid.NewGuid()), CancellationToken.None);

        _store.Rows.Should().OnlyContain(r => r.ReleasedAt != null);
        var act = () => new TargetLedgerOrderStateChangedHandler(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
