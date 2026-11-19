using FluentAssertions;
using GeoAssets.Core.Messaging;
using GeoAssets.Core.Messaging.Events;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Notifications;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Registry;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// The global target ledger end to end on real PostgreSQL (XD01-379): <see cref="TargetLedgerService"/> over the EF store,
/// unit of work, outbox and inbox — cross-vertical exclusivity (AC1), the terminal-release consumer applied exactly once
/// (AC2) and the events staged in the same transaction as the change (AC6).
/// </summary>
public sealed class TargetLedgerPersistenceTests
{
    private const string Database = "target_ledger_service";
    private static readonly Guid OrgX = new("c0c0c0c0-0000-4000-8000-000000000379");
    private static readonly Guid OrgY = new("d0d0d0d0-0000-4000-8000-000000000379");

    private static readonly GeoFeature[] Catalog =
    [
        TargetLedgerServiceHarness.Feature("f1", OrgX),
        TargetLedgerServiceHarness.Feature("f2", OrgX),
        TargetLedgerServiceHarness.Feature("f3", OrgX),
        TargetLedgerServiceHarness.Feature("g1", OrgY),
    ];

    private static InMemoryOrderTypeDescriptorRegistry Descriptors(OrderTargetExclusivity a, OrderTargetExclusivity b)
    {
        var registry = new InMemoryOrderTypeDescriptorRegistry([]);
        registry.Upsert("vertical-a", [LedgerEfHarness.Descriptor("vertical-a", "a-type", a, "Closed")]);
        registry.Upsert("vertical-b", [LedgerEfHarness.Descriptor("vertical-b", "b-type", b, "Done")]);
        return registry;
    }

    private static Task<LedgerEfHarness> SessionAsync(
        Guid org, InMemoryOrderTypeDescriptorRegistry descriptors, bool clear = false, Func<IOutboxWriter, IOutboxWriter>? decorate = null) =>
        LedgerEfHarness.CreateAsync(Database, org, Catalog, descriptors, clear, decorateOutbox: decorate);

    private static Task<TargetSelectionResult> ReserveAsync(LedgerEfHarness h, TargetLedgerOrder order, params string[] features) =>
        h.Service.ReserveAsync(order, TargetLedgerServiceHarness.ByIds(order.OrderId, features), "tester", CancellationToken.None);

    private static IntegrationMessageContext Context(Guid messageId) =>
        new IntegrationMessageContextBuilder().WithMessageId(messageId).WithMessageType(IntegrationEventTypes.OrderStateChangedV1).Build();

    private static OrderStateChangedEvent Terminal(
        string? vertical = "vertical-a", bool? isTerminal = true, Guid? org = null, string status = "Closed", string order = "a1") =>
        new(order, "a-type", "Open", status, "operator", LedgerEfHarness.Now, VerticalId: vertical, IsTerminal: isTerminal, OrganizationId: org ?? OrgX);

    private static async Task<int> InboxCountAsync(LedgerEfHarness h)
    {
        await using var read = PostgresTargetStoreDatabase.NewContext(h.ConnectionString);
        return await read.InboxProcessedMessages.CountAsync();
    }

    // ── AC1: cross-vertical exclusivity ───────────────────────────────────────

    [Fact]
    public async Task Reserve_VerticalBExclusiveOnAFeatureVerticalAHoldsExclusively_ConflictsAndWritesNothingForB()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.BlockOnOverlap, OrderTargetExclusivity.BlockOnOverlap);
        await using var a = await SessionAsync(OrgX, descriptors, clear: true);
        await using var b = await SessionAsync(OrgX, descriptors);
        await ReserveAsync(a, a.OrderOf("vertical-a", "a-type", "a1"), "f1", "f2");

        var act = () => ReserveAsync(b, b.OrderOf("vertical-b", "b-type", "b1"), "f1");

        var conflict = (await act.Should().ThrowAsync<TargetLedgerConflictException>()).Which.Conflicts.Should().ContainSingle().Subject;
        conflict.FeatureId.Should().Be("f1");
        conflict.VerticalId.Should().Be("vertical-a");
        conflict.OrderId.Should().Be("a1");
        (await a.RowsAsync()).Select(r => r.Vertical).Should().OnlyContain(v => v == "vertical-a").And.HaveCount(2);
        (await b.Service.GetPageAsync(new TargetLedgerKey("vertical-b", "b1"), OrgX, 0, 10, CancellationToken.None)).TotalCount.Should().Be(0);
        var staged = await a.StagedAsync();
        staged.Should().ContainSingle().Which.Should().Match<(string Type, string Key, object Event)>(m =>
            string.Equals(m.Type, IntegrationEventTypes.TargetsReservedV1, StringComparison.Ordinal) && string.Equals(m.Key, "vertical-a/a1", StringComparison.Ordinal), "no TargetsReservedV1 is staged for vertical B");
    }

    [Fact]
    public async Task Reserve_TwoVerticalsExclusiveOnTheSameFeatureAtTheSameMoment_ExactlyOneWins()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.BlockOnOverlap, OrderTargetExclusivity.BlockOnOverlap);

        for (var round = 0; round < 10; round++)
        {
            await using var a = await SessionAsync(OrgX, descriptors, clear: true);
            await using var b = await SessionAsync(OrgX, descriptors);
            using var start = new SemaphoreSlim(0, 2);

            async Task<bool> RaceAsync(LedgerEfHarness h, TargetLedgerOrder order)
            {
                await start.WaitAsync();
                try
                {
                    await ReserveAsync(h, order, "f3");
                    return true;
                }
                catch (TargetLedgerConflictException)
                {
                    return false;
                }
            }

            var raceA = RaceAsync(a, a.OrderOf("vertical-a", "a-type", "a1"));
            var raceB = RaceAsync(b, b.OrderOf("vertical-b", "b-type", "b1"));
            start.Release(2);
            var outcomes = await Task.WhenAll(raceA, raceB);

            outcomes.Count(won => won).Should().Be(1, $"the feature locks serialize the two reservations (round {round})");
            (await a.RowsAsync()).Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Reserve_ASharedPolicyHolder_DoesNotBlockAnotherVertical()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.None, OrderTargetExclusivity.None);
        await using var a = await SessionAsync(OrgX, descriptors, clear: true);
        await using var b = await SessionAsync(OrgX, descriptors);
        await ReserveAsync(a, a.OrderOf("vertical-a", "a-type", "a1"), "f1");

        await ReserveAsync(b, b.OrderOf("vertical-b", "b-type", "b1"), "f1");

        (await a.RowsAsync()).Select(r => (r.Vertical, r.Feature)).Should().Equal(("vertical-a", "f1"), ("vertical-b", "f1"));
        (await a.StagedAsync()).Should().HaveCount(2);
    }

    [Fact]
    public async Task Reserve_SameVerticalRepeat_IsIdempotent_NoDuplicateRowsAddedZeroAndNoEvent()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.BlockOnOverlap, OrderTargetExclusivity.None);
        await using var a = await SessionAsync(OrgX, descriptors, clear: true);
        var order = a.OrderOf("vertical-a", "a-type", "a1");
        await ReserveAsync(a, order, "f1", "f2");

        var again = await a.Service.ReserveAsync(order, TargetLedgerServiceHarness.ByIds("a1", "f1", "f2"), "tester", CancellationToken.None);

        again.Added.Should().Be(0);
        (await a.RowsAsync()).Should().HaveCount(2);
        (await a.StagedAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task Reserve_AKernelOrderHoldingTheFeatureExclusively_BlocksARemoteReserve()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.BlockOnOverlap, OrderTargetExclusivity.None);
        var types = new OrderTypeRegistry();
        types.Register(new OrderType
        {
            Id = "kernel-exclusive",
            DisplayName = "Kernel exclusive",
            TargetPolicy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"], Exclusivity = OrderTargetExclusivity.BlockOnOverlap },
        });
        await using var h = await LedgerEfHarness.CreateAsync(Database, OrgX, Catalog, descriptors, orderTypes: types);
        await new EFServiceOrderRepository(h.Db, orderTypeRegistry: types, timeProvider: h.Clock).AddAsync(new ServiceOrder
        {
            Id = "k1", OrderTypeId = "kernel-exclusive", OrganizationId = OrgX, CreatedBy = "u1", CreatedAt = LedgerEfHarness.Now.UtcDateTime,
        });
        await new EFServiceOrderTargetRepository(h.Db).AddRangeAsync(
            "k1", [new ServiceOrderTargetBuilder().WithId("k1:f1").WithOrderId("k1").WithFeatureId("f1").WithOrganizationId(OrgX).Build()], CancellationToken.None);

        var act = () => ReserveAsync(h, h.OrderOf("vertical-a", "a-type", "a1"), "f1");

        var conflict = (await act.Should().ThrowAsync<TargetLedgerConflictException>()).Which.Conflicts.Should().ContainSingle().Subject;
        (conflict.VerticalId, conflict.OrderId, conflict.OrderTypeId).Should().Be(("kernel", "k1", "kernel-exclusive"));
        (await h.RowsAsync()).Should().ContainSingle().Which.Vertical.Should().Be("kernel");
        (await h.StagedAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Reserve_AnotherOrganizationsExclusiveHolder_BlocksAndIsRedacted()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.BlockOnOverlap, OrderTargetExclusivity.BlockOnOverlap);
        var shared = TargetLedgerServiceHarness.Feature("shared", OrgY);
        await using var holder = await LedgerEfHarness.CreateAsync(Database, OrgY, [shared], descriptors);
        await ReserveAsync(holder, holder.OrderOf("vertical-a", "a-type", "a1"), "shared");
        await using var other = await LedgerEfHarness.CreateAsync(Database, OrgX, [TargetLedgerServiceHarness.Feature("shared", OrgX)], descriptors, clear: false);

        var act = () => ReserveAsync(other, other.OrderOf("vertical-b", "b-type", "b1"), "shared");

        var conflict = (await act.Should().ThrowAsync<TargetLedgerConflictException>()).Which.Conflicts.Should().ContainSingle().Subject;
        (conflict.VerticalId, conflict.OrderId, conflict.OrderTypeId).Should().Be(("vertical-a", null, null));
    }

    [Fact]
    public async Task Reserve_ReleasedHolder_NoLongerBlocks()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.BlockOnOverlap, OrderTargetExclusivity.BlockOnOverlap);
        await using var a = await SessionAsync(OrgX, descriptors, clear: true);
        var holder = a.OrderOf("vertical-a", "a-type", "a1");
        await ReserveAsync(a, holder, "f1");
        await a.Service.ReleaseAsync(holder.Key, OrgX, null, "tester", CancellationToken.None);
        await using var b = await SessionAsync(OrgX, descriptors);

        await ReserveAsync(b, b.OrderOf("vertical-b", "b-type", "b1"), "f1");

        (await b.RowsAsync()).Select(r => (r.Vertical, r.ReleasedAt != null)).Should().Equal(("vertical-a", true), ("vertical-b", false));
    }

    // ── AC2: terminal release exactly once ────────────────────────────────────

    [Fact]
    public async Task ReleaseTerminal_SameMessageHandledTwice_ReleasesOnceStagesOneEventAndTheSecondCallReturnsFalse()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.BlockOnOverlap, OrderTargetExclusivity.None);
        await using var h = await SessionAsync(OrgX, descriptors, clear: true);
        await ReserveAsync(h, h.OrderOf("vertical-a", "a-type", "a1"), "f1", "f2");
        var messageId = Guid.NewGuid();

        var first = await h.Service.ReleaseTerminalAsync(Terminal(), Context(messageId), CancellationToken.None);
        var second = await h.Service.ReleaseTerminalAsync(Terminal(), Context(messageId), CancellationToken.None);

        first.Should().BeTrue();
        second.Should().BeFalse();
        (await h.RowsAsync()).Should().HaveCount(2).And.OnlyContain(r => r.ReleasedAt == LedgerEfHarness.Now.UtcDateTime);
        var released = (await h.StagedAsync()).Where(m => string.Equals(m.MessageType, IntegrationEventTypes.TargetsReleasedV1, StringComparison.Ordinal)).Should().ContainSingle().Subject;
        released.PartitionKey.Should().Be("vertical-a/a1");
        ((TargetsReleasedEvent)released.Event).Targets.Select(t => t.FeatureId).Should().BeEquivalentTo("f1", "f2");
        ((TargetsReleasedEvent)released.Event).PerformedBy.Should().Be("operator");
        (await InboxCountAsync(h)).Should().Be(1);
    }

    [Fact]
    public async Task ReleaseTerminal_IsTerminalNull_UsesTheDescriptorsTerminalStatuses()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.BlockOnOverlap, OrderTargetExclusivity.None);
        await using var h = await SessionAsync(OrgX, descriptors, clear: true);
        await ReserveAsync(h, h.OrderOf("vertical-a", "a-type", "a1"), "f1");

        (await h.Service.ReleaseTerminalAsync(Terminal(isTerminal: null, status: "Reviewing"), Context(Guid.NewGuid()), CancellationToken.None)).Should().BeFalse();
        (await h.RowsAsync()).Should().OnlyContain(r => r.ReleasedAt == null);
        (await h.Service.ReleaseTerminalAsync(Terminal(isTerminal: null, status: "Closed"), Context(Guid.NewGuid()), CancellationToken.None)).Should().BeTrue();

        (await h.RowsAsync()).Should().OnlyContain(r => r.ReleasedAt != null);
    }

    [Fact]
    public async Task ReleaseTerminal_IsTerminalFalse_ReleasesNothing_AndDoesNotConsumeTheInbox()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.BlockOnOverlap, OrderTargetExclusivity.None);
        await using var h = await SessionAsync(OrgX, descriptors, clear: true);
        await ReserveAsync(h, h.OrderOf("vertical-a", "a-type", "a1"), "f1");
        var messageId = Guid.NewGuid();

        (await h.Service.ReleaseTerminalAsync(Terminal(isTerminal: false, status: "Closed"), Context(messageId), CancellationToken.None)).Should().BeFalse();

        (await h.RowsAsync()).Should().OnlyContain(r => r.ReleasedAt == null);
        (await InboxCountAsync(h)).Should().Be(0);
        (await h.Service.ReleaseTerminalAsync(Terminal(), Context(messageId), CancellationToken.None)).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("kernel")]
    [InlineData("Not Well Formed")]
    public async Task ReleaseTerminal_VerticalNullKernelOrMalformed_IsIgnored(string? vertical)
    {
        var descriptors = Descriptors(OrderTargetExclusivity.BlockOnOverlap, OrderTargetExclusivity.None);
        await using var h = await SessionAsync(OrgX, descriptors, clear: true);
        await ReserveAsync(h, h.OrderOf("vertical-a", "a-type", "a1"), "f1");

        (await h.Service.ReleaseTerminalAsync(Terminal(vertical), Context(Guid.NewGuid()), CancellationToken.None)).Should().BeFalse();

        (await h.RowsAsync()).Should().OnlyContain(r => r.ReleasedAt == null);
        (await InboxCountAsync(h)).Should().Be(0);
    }

    [Fact]
    public async Task ReleaseTerminal_OnlyTheNamedVerticalsOrder_IsReleased()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.None, OrderTargetExclusivity.None);
        await using var h = await SessionAsync(OrgX, descriptors, clear: true);
        await ReserveAsync(h, h.OrderOf("vertical-a", "a-type", "same"), "f1");
        await ReserveAsync(h, h.OrderOf("vertical-b", "b-type", "same"), "f1");

        await h.Service.ReleaseTerminalAsync(Terminal(order: "same"), Context(Guid.NewGuid()), CancellationToken.None);

        (await h.RowsAsync()).Select(r => (r.Vertical, r.ReleasedAt != null)).Should().Equal(("vertical-a", true), ("vertical-b", false));
    }

    // ── AC6: events staged in the same transaction ────────────────────────────

    [Fact]
    public async Task ReserveReleaseCommit_EachStageExactlyOneEventWithTheChangedTargetIds_TypeAndPartitionKey()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.None, OrderTargetExclusivity.None);
        await using var h = await SessionAsync(OrgX, descriptors, clear: true);
        var order = h.OrderOf("vertical-a", "a-type", "a1");
        await ReserveAsync(h, order, "f1", "f2", "f3");

        await h.Service.CommitAsync(order.Key, OrgX, [TargetLedger.TargetId("vertical-a", "a1", "f1")], "tester", CancellationToken.None);
        await h.Service.ReleaseAsync(order.Key, OrgX, [TargetLedger.TargetId("vertical-a", "a1", "f2")], "tester", CancellationToken.None);

        var staged = await h.StagedAsync();
        staged.Select(m => m.MessageType).Should().BeEquivalentTo(
            [IntegrationEventTypes.TargetsReservedV1, IntegrationEventTypes.TargetsCommittedV1, IntegrationEventTypes.TargetsReleasedV1]);
        staged.Should().OnlyContain(m => m.PartitionKey == "vertical-a/a1");
        var byType = staged.ToDictionary(m => m.MessageType, m => (TargetLedgerEvent)m.Event, StringComparer.Ordinal);
        byType[IntegrationEventTypes.TargetsReservedV1].Targets.Select(t => t.FeatureId).Should().BeEquivalentTo("f1", "f2", "f3");
        byType[IntegrationEventTypes.TargetsCommittedV1].Targets.Select(t => t.FeatureId).Should().Equal("f1");
        byType[IntegrationEventTypes.TargetsReleasedV1].Targets.Select(t => t.FeatureId).Should().Equal("f2");
        byType.Values.Should().OnlyContain(e => e.VerticalId == "vertical-a" && e.OrderId == "a1" && e.OrganizationId == OrgX && e.PerformedBy == "tester");
    }

    [Fact]
    public async Task ReleaseAndCommit_ThatChangeNothing_StageNoEvent()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.None, OrderTargetExclusivity.None);
        await using var h = await SessionAsync(OrgX, descriptors, clear: true);
        var order = h.OrderOf("vertical-a", "a-type", "a1");
        await ReserveAsync(h, order, "f1");
        await h.Service.CommitAsync(order.Key, OrgX, null, "tester", CancellationToken.None);
        await h.Service.ReleaseAsync(order.Key, OrgX, null, "tester", CancellationToken.None);
        var before = (await h.StagedAsync()).Count;

        await h.Service.CommitAsync(order.Key, OrgX, null, "tester", CancellationToken.None);
        await h.Service.ReleaseAsync(order.Key, OrgX, null, "tester", CancellationToken.None);

        before.Should().Be(3);
        (await h.StagedAsync()).Should().HaveCount(before);
    }

    [Fact]
    public async Task Reserve_WhenStagingTheEventFailsAfterTheInsert_RollsBackTheTargetsAndTheOutboxTogether()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.None, OrderTargetExclusivity.None);
        await using var h = await SessionAsync(OrgX, descriptors, clear: true, decorate: inner => new FailingAfterStagingOutbox(inner));

        var act = () => ReserveAsync(h, h.OrderOf("vertical-a", "a-type", "a1"), "f1", "f2");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("staging failed");
        (await h.RowsAsync()).Should().BeEmpty("the insert and its event are one transaction");
        (await h.StagedAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ReleaseAndCommit_WhenStagingFailsAfterTheUpdate_RollBackTheChange()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.None, OrderTargetExclusivity.None);
        await using var seed = await SessionAsync(OrgX, descriptors, clear: true);
        var order = seed.OrderOf("vertical-a", "a-type", "a1");
        await ReserveAsync(seed, order, "f1");
        await using var failing = await SessionAsync(OrgX, descriptors, decorate: inner => new FailingAfterStagingOutbox(inner));

        var commit = () => failing.Service.CommitAsync(order.Key, OrgX, null, "tester", CancellationToken.None);
        var release = () => failing.Service.ReleaseAsync(order.Key, OrgX, null, "tester", CancellationToken.None);

        await commit.Should().ThrowAsync<InvalidOperationException>();
        await release.Should().ThrowAsync<InvalidOperationException>();
        (await seed.RowsAsync()).Should().ContainSingle().Which.Should().Match<(string Vertical, string Order, string Feature, DateTime? ReleasedAt, string Status)>(
            r => r.ReleasedAt == null && r.Status == OrderTargetStatus.Pending);
        (await seed.StagedAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ReleaseTerminal_WhenStagingFailsAfterTheRelease_RollsBackTheReleaseAndTheInboxRecord_SoARedeliveryApplies()
    {
        var descriptors = Descriptors(OrderTargetExclusivity.BlockOnOverlap, OrderTargetExclusivity.None);
        await using var seed = await SessionAsync(OrgX, descriptors, clear: true);
        await ReserveAsync(seed, seed.OrderOf("vertical-a", "a-type", "a1"), "f1");
        var messageId = Guid.NewGuid();
        await using var failing = await SessionAsync(OrgX, descriptors, decorate: inner => new FailingAfterStagingOutbox(inner));

        var act = () => failing.Service.ReleaseTerminalAsync(Terminal(), Context(messageId), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await seed.RowsAsync()).Should().OnlyContain(r => r.ReleasedAt == null);
        (await InboxCountAsync(seed)).Should().Be(0);
        await using var retry = await SessionAsync(OrgX, descriptors);
        (await retry.Service.ReleaseTerminalAsync(Terminal(), Context(messageId), CancellationToken.None)).Should().BeTrue();
    }

    /// <summary>Stages through the real outbox, then fails: the point of no return is after the event was written.</summary>
    private sealed class FailingAfterStagingOutbox(IOutboxWriter inner) : IOutboxWriter
    {
        public async Task<Guid> EnqueueAsync<TEvent>(
            TEvent message,
            string messageType,
            string partitionKey,
            string? catalogInstanceId = null,
            string? correlationId = null,
            string? causationId = null,
            CancellationToken ct = default)
            where TEvent : notnull
        {
            await inner.EnqueueAsync(message, messageType, partitionKey, catalogInstanceId, correlationId, causationId, ct).ConfigureAwait(false);
            throw new InvalidOperationException("staging failed");
        }

        public Task EnqueueEnvelopeAsync(IntegrationEnvelope envelope, CancellationToken ct = default) => inner.EnqueueEnvelopeAsync(envelope, ct);
    }
}
