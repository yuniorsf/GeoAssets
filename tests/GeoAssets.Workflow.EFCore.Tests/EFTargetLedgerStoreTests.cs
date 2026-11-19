using FluentAssertions;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Registry;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary><see cref="EFTargetLedgerStore"/> (XD01-379) on a real PostgreSQL: the port's contract, member by member.</summary>
public sealed class EFTargetLedgerStoreTests
{
    private const string Database = "target_ledger_store";
    private static readonly Guid OrgX = new("c0c0c0c0-0000-4000-8000-000000000379");
    private static readonly Guid OrgY = new("d0d0d0d0-0000-4000-8000-000000000379");
    private static readonly DateTimeOffset Later = new(2026, 11, 20, 8, 0, 0, TimeSpan.Zero);

    private static Task<LedgerEfHarness> HarnessAsync(Guid? org = null, OrderTypeRegistry? orderTypes = null) =>
        LedgerEfHarness.CreateAsync(Database, org ?? OrgX, [], new InMemoryOrderTypeDescriptorRegistry([]), orderTypes: orderTypes);

    private static TargetLedgerOrder Order(string vertical, string orderId, OrderTargetExclusivity exclusivity = OrderTargetExclusivity.None, Guid? org = null) =>
        new(vertical, orderId, $"{vertical}-type", org ?? OrgX, new OrderTargetPolicy { TargetAssetTypeIds = ["pole"], Exclusivity = exclusivity });

    private static ServiceOrderTarget Target(TargetLedgerOrder order, string feature, string status = OrderTargetStatus.Pending) =>
        new ServiceOrderTargetBuilder()
            .WithId(TargetLedger.TargetId(order.VerticalId, order.OrderId, feature))
            .WithOrderId(order.OrderId)
            .WithFeatureId(feature)
            .WithOrganizationId(order.OrganizationId)
            .WithStatus(status)
            .Build() with { VerticalId = order.VerticalId };

    [Fact]
    public async Task ReserveAsync_InsertsRemoteTargets_ReturnsOnlyTheInsertedOnes_AndRepeatInsertsNothing()
    {
        await using var h = await HarnessAsync();
        var order = Order("isp", "o1", OrderTargetExclusivity.BlockOnOverlap);

        var first = await h.Store.ReserveAsync(order, [Target(order, "f1"), Target(order, "f2")], CancellationToken.None);
        var second = await h.Store.ReserveAsync(order, [Target(order, "f1"), Target(order, "f3")], CancellationToken.None);
        var third = await h.Store.ReserveAsync(order, [Target(order, "f1"), Target(order, "f3")], CancellationToken.None);

        first.Select(t => t.FeatureId).Should().BeEquivalentTo("f1", "f2");
        first.Should().OnlyContain(t => t.VerticalId == "isp" && t.OrganizationId == OrgX);
        second.Select(t => t.FeatureId).Should().Equal("f3");
        third.Should().BeEmpty();
        (await h.RowsAsync()).Select(r => r.Feature).Should().Equal("f1", "f2", "f3");
    }

    [Fact]
    public async Task ReserveAsync_StoresTheOrderTypeAndExclusivity_AndNoKernelOrderId()
    {
        await using var h = await HarnessAsync();
        var order = Order("isp", "o1", OrderTargetExclusivity.BlockOnOverlap);

        await h.Store.ReserveAsync(order, [Target(order, "f1")], CancellationToken.None);

        await using var read = PostgresTargetStoreDatabase.NewContext(h.ConnectionString);
        var row = await read.ServiceOrderTargets.IgnoreQueryFilters().SingleAsync();
        row.VerticalId.Should().Be("isp");
        row.OrderTypeId.Should().Be("isp-type");
        row.Exclusivity.Should().Be((int)OrderTargetExclusivity.BlockOnOverlap);
        row.KernelOrderId.Should().BeNull();
    }

    [Fact]
    public async Task ReserveAsync_TwoVerticalsWithTheSameOrderIdAndFeature_BothStore()
    {
        await using var h = await HarnessAsync();
        var a = Order("vertical-a", "same-id");
        var b = Order("vertical-b", "same-id");

        await h.Store.ReserveAsync(a, [Target(a, "f1")], CancellationToken.None);
        await h.Store.ReserveAsync(b, [Target(b, "f1")], CancellationToken.None);

        (await h.RowsAsync()).Select(r => r.Vertical).Should().Equal("vertical-a", "vertical-b");
    }

    [Fact]
    public async Task ReserveAsync_DuplicateFeaturesInOneCall_StoreOnce()
    {
        await using var h = await HarnessAsync();
        var order = Order("isp", "o1");

        var inserted = await h.Store.ReserveAsync(order, [Target(order, "f1"), Target(order, "f1")], CancellationToken.None);

        inserted.Should().ContainSingle();
    }

    [Fact]
    public async Task ReserveAsync_ManyTargets_AreInsertedInBatches()
    {
        await using var h = await HarnessAsync();
        var order = Order("isp", "o1");
        var targets = Enumerable.Range(0, EFServiceOrderTargetRepository.InsertRowsPerStatement * 2 + 7).Select(i => Target(order, $"f{i:D4}")).ToList();

        var inserted = await h.Store.ReserveAsync(order, targets, CancellationToken.None);

        inserted.Should().HaveCount(targets.Count);
    }

    [Fact]
    public async Task ReserveAsync_TargetOfAnotherVerticalOrderOrOrganization_ThrowsArgumentException_AndStoresNothing()
    {
        await using var h = await HarnessAsync();
        var order = Order("isp", "o1");
        var elsewhere = Order("other", "o1");
        var otherOrder = Order("isp", "o2");
        var otherOrg = Order("isp", "o1", org: OrgY);

        foreach (var wrong in new[] { elsewhere, otherOrder, otherOrg })
        {
            var act = () => h.Store.ReserveAsync(order, [Target(order, "f1"), Target(wrong, "f2")], CancellationToken.None);
            await act.Should().ThrowAsync<ArgumentException>();
        }

        (await h.RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task ReserveAsync_KernelOrderWithoutAnOrderRow_IsRejectedByTheDatabase()
    {
        await using var h = await HarnessAsync();
        var order = Order("kernel", "no-such-order");

        var act = () => h.Store.ReserveAsync(order, [Target(order, "f1")], CancellationToken.None);

        (await act.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("FK_ServiceOrderTargets_ServiceOrders_KernelOrderId");
    }

    [Fact]
    public async Task ReserveAsync_NullArguments_AndCancelledToken_Throw()
    {
        await using var h = await HarnessAsync();
        var order = Order("isp", "o1");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var nullOrder = () => h.Store.ReserveAsync(null!, [], CancellationToken.None);
        var nullTargets = () => h.Store.ReserveAsync(order, null!, CancellationToken.None);
        var cancelled = () => h.Store.ReserveAsync(order, [Target(order, "f1")], cts.Token);

        await nullOrder.Should().ThrowAsync<ArgumentNullException>();
        await nullTargets.Should().ThrowAsync<ArgumentNullException>();
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
        (await h.RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task FindOpenUsagesAsync_ReportsEveryVerticalAndOrganization_ExceptTheExcludedOrder_AndReleasedOnes()
    {
        await using var h = await HarnessAsync();
        var a = Order("vertical-a", "a1", OrderTargetExclusivity.BlockOnOverlap);
        var b = Order("vertical-b", "b1", OrderTargetExclusivity.WarnOnOverlap, OrgY);
        var released = Order("vertical-c", "c1", OrderTargetExclusivity.BlockOnOverlap);
        await h.Store.ReserveAsync(a, [Target(a, "f1"), Target(a, "f2")], CancellationToken.None);
        await h.Store.ReserveAsync(b, [Target(b, "f1")], CancellationToken.None);
        await h.Store.ReserveAsync(released, [Target(released, "f1")], CancellationToken.None);
        await h.Store.ReleaseAsync(released.Key, null, null, Later, CancellationToken.None);

        var all = await h.Store.FindOpenUsagesAsync(["f1"], new TargetLedgerKey("nobody", "none"), CancellationToken.None);
        var excludingA = await h.Store.FindOpenUsagesAsync(["f1", "f2"], a.Key, CancellationToken.None);

        all.Select(u => (u.Usage.VerticalId, u.Usage.OrderId, u.OrganizationId, u.Usage.Exclusivity, u.Usage.OrderTypeId)).Should().BeEquivalentTo(
        [
            ("vertical-a", "a1", OrgX, OrderTargetExclusivity.BlockOnOverlap, "vertical-a-type"),
            ("vertical-b", "b1", OrgY, OrderTargetExclusivity.WarnOnOverlap, "vertical-b-type"),
        ]);
        all.Should().OnlyContain(u => u.Usage.OrderStatus.Length == 0 && u.Usage.ParentOrderId == null);
        excludingA.Select(u => u.Usage.VerticalId).Should().Equal("vertical-b");
    }

    [Fact]
    public async Task FindOpenUsagesAsync_SameOrderIdInAnotherVertical_IsNotExcluded()
    {
        await using var h = await HarnessAsync();
        var a = Order("vertical-a", "same");
        var b = Order("vertical-b", "same");
        await h.Store.ReserveAsync(a, [Target(a, "f1")], CancellationToken.None);
        await h.Store.ReserveAsync(b, [Target(b, "f1")], CancellationToken.None);

        var usages = await h.Store.FindOpenUsagesAsync(["f1"], a.Key, CancellationToken.None);

        usages.Should().ContainSingle().Which.Usage.VerticalId.Should().Be("vertical-b");
    }

    [Fact]
    public async Task FindOpenUsagesAsync_KernelHolder_IsReportedWithItsOrderTypeStatusParentAndExclusivity()
    {
        var types = new OrderTypeRegistry();
        types.Register(new OrderType
        {
            Id = "kernel-exclusive",
            DisplayName = "Kernel exclusive",
            TargetPolicy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"], Exclusivity = OrderTargetExclusivity.BlockOnOverlap },
        });
        await using var h = await HarnessAsync(orderTypes: types);
        await SeedKernelHolderAsync(h, "k1", "f1");

        var usages = await h.Store.FindOpenUsagesAsync(["f1"], new TargetLedgerKey("isp", "o1"), CancellationToken.None);

        var usage = usages.Should().ContainSingle().Subject;
        usage.Usage.Should().Be(new OrderTargetUsage("f1", "k1", "kernel-exclusive", ServiceOrderStatus.Draft, null, OrderTargetExclusivity.BlockOnOverlap));
        usage.OrganizationId.Should().Be(OrgX);
    }

    [Fact]
    public async Task FindOpenUsagesAsync_EmptyFeatureIds_ReturnsEmptyWithoutQuerying_AndNullsThrow()
    {
        await using var h = await HarnessAsync();

        (await h.Store.FindOpenUsagesAsync([], new TargetLedgerKey("v", "o"), CancellationToken.None)).Should().BeEmpty();
        var nullIds = () => h.Store.FindOpenUsagesAsync(null!, new TargetLedgerKey("v", "o"), CancellationToken.None);
        var nullKey = () => h.Store.FindOpenUsagesAsync(["f"], null!, CancellationToken.None);

        await nullIds.Should().ThrowAsync<ArgumentNullException>();
        await nullKey.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task FindOrganizationAsync_IsTheOrganizationOfTheOrdersTargets_OrNull()
    {
        await using var h = await HarnessAsync();
        var order = Order("isp", "o1", org: OrgY);
        await h.Store.ReserveAsync(order, [Target(order, "f1")], CancellationToken.None);

        (await h.Store.FindOrganizationAsync(order.Key, CancellationToken.None)).Should().Be(OrgY);
        (await h.Store.FindOrganizationAsync(new TargetLedgerKey("isp", "o2"), CancellationToken.None)).Should().BeNull();
        (await h.Store.FindOrganizationAsync(new TargetLedgerKey("other", "o1"), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task GetPageAsync_OrdersByFeatureThenId_PagesWithATotal_IncludesReleased_AndHidesOtherOrganizations()
    {
        await using var h = await HarnessAsync();
        var order = Order("isp", "o1");
        await h.Store.ReserveAsync(order, [Target(order, "f3"), Target(order, "f1"), Target(order, "f2")], CancellationToken.None);
        await h.Store.ReleaseAsync(order.Key, OrgX, [TargetLedger.TargetId("isp", "o1", "f1")], Later, CancellationToken.None);

        var first = await h.Store.GetPageAsync(order.Key, OrgX, 0, 2, CancellationToken.None);
        var second = await h.Store.GetPageAsync(order.Key, OrgX, 2, 2, CancellationToken.None);
        var beyond = await h.Store.GetPageAsync(order.Key, OrgX, 10, 2, CancellationToken.None);
        var other = await h.Store.GetPageAsync(order.Key, OrgY, 0, 10, CancellationToken.None);

        first.TotalCount.Should().Be(3);
        first.Items.Select(e => e.Target.FeatureId).Should().Equal("f1", "f2");
        first.Items[0].ReleasedAt.Should().Be(Later.UtcDateTime);
        first.Items[1].ReleasedAt.Should().BeNull();
        second.Items.Select(e => e.Target.FeatureId).Should().Equal("f3");
        beyond.Items.Should().BeEmpty();
        beyond.TotalCount.Should().Be(3);
        other.TotalCount.Should().Be(0);
        other.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetPageAsync_InvalidPaging_Throws()
    {
        await using var h = await HarnessAsync();

        var negativeSkip = () => h.Store.GetPageAsync(new TargetLedgerKey("v", "o"), OrgX, -1, 10, CancellationToken.None);
        var zeroTake = () => h.Store.GetPageAsync(new TargetLedgerKey("v", "o"), OrgX, 0, 0, CancellationToken.None);

        await negativeSkip.Should().ThrowAsync<ArgumentOutOfRangeException>();
        await zeroTake.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ReleaseAsync_ReleasesOpenTargetsOnce_ScopedByOrganizationAndIds()
    {
        await using var h = await HarnessAsync();
        var order = Order("isp", "o1");
        await h.Store.ReserveAsync(order, [Target(order, "f1"), Target(order, "f2"), Target(order, "f3")], CancellationToken.None);

        (await h.Store.ReleaseAsync(order.Key, OrgY, null, Later, CancellationToken.None)).Should().BeEmpty("another organization releases nothing");
        var some = await h.Store.ReleaseAsync(order.Key, OrgX, [TargetLedger.TargetId("isp", "o1", "f1"), "no-such-target"], Later, CancellationToken.None);
        var rest = await h.Store.ReleaseAsync(order.Key, null, null, Later.AddHours(1), CancellationToken.None);
        var again = await h.Store.ReleaseAsync(order.Key, null, null, Later.AddHours(2), CancellationToken.None);

        some.Select(t => t.FeatureId).Should().Equal("f1");
        rest.Select(t => t.FeatureId).Should().BeEquivalentTo("f2", "f3");
        again.Should().BeEmpty();
        (await h.RowsAsync()).Select(r => r.ReleasedAt).Should().BeEquivalentTo([Later.UtcDateTime, Later.UtcDateTime.AddHours(1), Later.UtcDateTime.AddHours(1)]);
    }

    [Fact]
    public async Task ReleaseAsync_EmptyIdList_ReleasesNothing()
    {
        await using var h = await HarnessAsync();
        var order = Order("isp", "o1");
        await h.Store.ReserveAsync(order, [Target(order, "f1")], CancellationToken.None);

        (await h.Store.ReleaseAsync(order.Key, OrgX, [], Later, CancellationToken.None)).Should().BeEmpty();
        (await h.Store.ReleaseAsync(order.Key, OrgX, [" "], Later, CancellationToken.None)).Should().BeEmpty();
        (await h.RowsAsync()).Should().OnlyContain(r => r.ReleasedAt == null);
    }

    [Fact]
    public async Task ReleaseAsync_ManyIds_AreSplitIntoChunks()
    {
        await using var h = await HarnessAsync();
        var order = Order("isp", "o1");
        var features = Enumerable.Range(0, 30).Select(i => $"f{i:D3}").ToList();
        await h.Store.ReserveAsync(order, [.. features.Select(f => Target(order, f))], CancellationToken.None);
        var ids = Enumerable.Range(0, EFServiceOrderTargetRepository.IdChunkSize + 50).Select(i => $"missing-{i}").Concat(features.Select(f => TargetLedger.TargetId("isp", "o1", f))).ToList();

        var released = await h.Store.ReleaseAsync(order.Key, OrgX, ids, Later, CancellationToken.None);

        released.Should().HaveCount(30);
    }

    [Fact]
    public async Task CommitAsync_MovesPendingAndInProgressToDone_BumpsTheVersion_AndKeepsHoldingTheFeature()
    {
        await using var h = await HarnessAsync();
        var order = Order("isp", "o1");
        await h.Store.ReserveAsync(
            order,
            [Target(order, "f1"), Target(order, "f2", OrderTargetStatus.InProgress), Target(order, "f3", OrderTargetStatus.Skipped), Target(order, "f4", OrderTargetStatus.Done)],
            CancellationToken.None);

        var committed = await h.Store.CommitAsync(order.Key, OrgX, null, CancellationToken.None);
        var again = await h.Store.CommitAsync(order.Key, OrgX, null, CancellationToken.None);

        committed.Select(t => t.FeatureId).Should().BeEquivalentTo("f1", "f2");
        committed.Should().OnlyContain(t => t.Status == OrderTargetStatus.Done && t.Version == 1);
        again.Should().BeEmpty();
        (await h.Store.FindOpenUsagesAsync(["f1"], new TargetLedgerKey("x", "y"), CancellationToken.None)).Should().ContainSingle();
        (await h.RowsAsync()).Select(r => (r.Feature, r.Status)).Should().Equal(
            ("f1", OrderTargetStatus.Done), ("f2", OrderTargetStatus.Done), ("f3", OrderTargetStatus.Skipped), ("f4", OrderTargetStatus.Done));
    }

    [Fact]
    public async Task CommitAsync_AnotherOrganizationAndReleasedTargets_AreUntouched_AndIdsScopeTheCommit()
    {
        await using var h = await HarnessAsync();
        var order = Order("isp", "o1");
        await h.Store.ReserveAsync(order, [Target(order, "f1"), Target(order, "f2")], CancellationToken.None);
        await h.Store.ReleaseAsync(order.Key, OrgX, [TargetLedger.TargetId("isp", "o1", "f2")], Later, CancellationToken.None);

        (await h.Store.CommitAsync(order.Key, OrgY, null, CancellationToken.None)).Should().BeEmpty();
        (await h.Store.CommitAsync(order.Key, OrgX, [TargetLedger.TargetId("isp", "o1", "f2")], CancellationToken.None)).Should().BeEmpty("a released target is not committed");
        (await h.Store.CommitAsync(order.Key, OrgX, [TargetLedger.TargetId("isp", "o1", "f1")], CancellationToken.None)).Should().ContainSingle();
    }

    [Fact]
    public async Task LedgerWrites_NeverTouchKernelRowsOfTheSameOrderId_AndInProcessRepositoryNeverTouchesRemoteRows()
    {
        await using var h = await HarnessAsync();
        await SeedKernelHolderAsync(h, "same", "f1");
        var remote = Order("isp", "same");
        await h.Store.ReserveAsync(remote, [Target(remote, "f1")], CancellationToken.None);

        var released = await h.Store.ReleaseAsync(remote.Key, OrgX, null, Later, CancellationToken.None);
        var kernel = new EFServiceOrderTargetRepository(h.Db);
        var kernelTargets = await kernel.GetPageAsync(new OrderTargetQuery("same"), CancellationToken.None);

        released.Should().ContainSingle().Which.VerticalId.Should().Be("isp");
        kernelTargets.Items.Should().ContainSingle().Which.VerticalId.Should().Be("kernel");
        (await kernel.CountAsync("same", CancellationToken.None)).Should().Be(1);
        (await kernel.RemoveAsync("same", [TargetLedger.TargetId("isp", "same", "f1")], CancellationToken.None)).Should().Be(0);
        (await h.RowsAsync()).Select(r => (r.Vertical, r.ReleasedAt != null)).Should().Equal(("isp", true), ("kernel", false));
    }

    [Fact]
    public async Task InProcessFindOpenUsagesAsync_ReportsARemoteHolderOfTheSameOrganization_NotAnotherOrganizations()
    {
        await using var h = await HarnessAsync();
        await SeedKernelHolderAsync(h, "k1", "f9");
        var sameOrg = Order("vertical-a", "a1", OrderTargetExclusivity.BlockOnOverlap);
        var otherOrg = Order("vertical-b", "b1", OrderTargetExclusivity.BlockOnOverlap, OrgY);
        await h.Store.ReserveAsync(sameOrg, [Target(sameOrg, "f1")], CancellationToken.None);
        await h.Store.ReserveAsync(otherOrg, [Target(otherOrg, "f1")], CancellationToken.None);

        var usages = await new EFServiceOrderTargetRepository(h.Db).FindOpenUsagesAsync(["f1"], "k1", CancellationToken.None);

        usages.Should().ContainSingle().Which.Should().Be(
            new OrderTargetUsage("f1", "a1", "vertical-a-type", string.Empty, null, OrderTargetExclusivity.BlockOnOverlap, "vertical-a"));
    }

    [Fact]
    public async Task InProcessAddRangeAsync_TargetOfARemoteVertical_IsRejected()
    {
        await using var h = await HarnessAsync();
        await SeedKernelHolderAsync(h, "k1", "f1");
        var remote = Order("isp", "k1");

        var act = () => new EFServiceOrderTargetRepository(h.Db).AddRangeAsync("k1", [Target(remote, "f2")], CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DeletingAnOrder_DeletesItsKernelTargets_ButNotARemoteVerticalsWithTheSameId()
    {
        await using var h = await HarnessAsync();
        await SeedKernelHolderAsync(h, "same", "f1");
        var remote = Order("isp", "same");
        await h.Store.ReserveAsync(remote, [Target(remote, "f1")], CancellationToken.None);

        await h.Db.Database.ExecuteSqlRawAsync("""DELETE FROM "ServiceOrders" WHERE "Id" = 'same'""");

        (await h.RowsAsync()).Select(r => r.Vertical).Should().Equal("isp");
    }

    [Fact]
    public async Task Schema_CheckConstraintRejectsARemoteRowWithAKernelOrderId_AndAKernelRowWithAMismatchedOne()
    {
        await using var h = await HarnessAsync();
        await SeedKernelHolderAsync(h, "k1", "f1");

        var remote = () => h.Db.Database.ExecuteSqlRawAsync(InsertSql("isp", "r1", "'k1'"));
        var mismatch = () => h.Db.Database.ExecuteSqlRawAsync(InsertSql("kernel", "k1", "NULL"));

        (await remote.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("CK_ServiceOrderTargets_KernelOrderId");
        (await mismatch.Should().ThrowAsync<Exception>()).Which.ToString().Should().Contain("CK_ServiceOrderTargets_KernelOrderId");
    }

    private static string InsertSql(string vertical, string orderId, string kernelOrderId) =>
        $"""
        INSERT INTO "ServiceOrderTargets" ("Id", "ServiceOrderId", "VerticalId", "KernelOrderId", "FeatureId", "AssetTypeId", "ActivityIdsJson", "Status", "ConflictLevel", "AddedAt", "AddedBy", "OrganizationId", "Version")
        VALUES ('raw-{vertical}-{orderId}', '{orderId}', '{vertical}', {kernelOrderId}, 'fx', 'pole', '[]', 'Pending', 0, now(), 'raw', '{OrgX}', 0)
        """;

    /// <summary>An in-process order of type <c>kernel-exclusive</c> holding <paramref name="featureId"/>.</summary>
    private static async Task SeedKernelHolderAsync(LedgerEfHarness h, string orderId, string featureId)
    {
        var types = new OrderTypeRegistry();
        await new EFServiceOrderRepository(h.Db, orderTypeRegistry: types, timeProvider: h.Clock).AddAsync(new ServiceOrder
        {
            Id = orderId, OrderTypeId = "kernel-exclusive", OrganizationId = OrgX, CreatedBy = "u1", CreatedAt = LedgerEfHarness.Now.UtcDateTime,
        });
        await new EFServiceOrderTargetRepository(h.Db).AddRangeAsync(
            orderId,
            [new ServiceOrderTargetBuilder().WithId($"{orderId}:{featureId}").WithOrderId(orderId).WithFeatureId(featureId).WithOrganizationId(OrgX).Build()],
            CancellationToken.None);
    }
}
