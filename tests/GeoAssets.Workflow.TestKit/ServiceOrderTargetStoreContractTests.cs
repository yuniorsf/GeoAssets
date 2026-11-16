using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.TestKit;

/// <summary>
/// Liskov Substitution Principle behavioural contract for every
/// <see cref="IServiceOrderTargetReader"/> + <see cref="IServiceOrderTargetWriter"/> adapter
/// (XD01-306). Each fact pins one sentence of the member documentation on those two
/// interfaces, so an adapter that passes here can replace any other.
///
/// Subclass it, implement <see cref="CreateHarnessAsync"/>, and declare at least one
/// <c>[Fact]</c> of your own on the subclass — xUnit only discovers <c>public</c> classes, and a
/// class whose tests are all inherited is the shape analyzers push back to <c>internal</c>.
///
/// The facts are split over three files: adding (here), updating and removing
/// (<c>.Writes.cs</c>), and reading (<c>.Reads.cs</c>).
/// </summary>
public abstract partial class ServiceOrderTargetStoreContractTests
{
    /// <summary>An organization that is never the harness's own tenant.</summary>
    protected static readonly Guid OtherOrganizationId = new("0b0b0b0b-0000-4000-8000-000000000002");

    private static readonly DateTime CreatedAt = new(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>A fresh, empty store with its order store and registry.</summary>
    protected abstract Task<TargetStoreContractHarness> CreateHarnessAsync();

    // ── Arrange helpers ───────────────────────────────────────────────────────

    private static Task SeedOrderAsync(
        TargetStoreContractHarness harness,
        string id,
        string orderTypeId = "inspection",
        string? parentOrderId = null,
        Guid? organizationId = null,
        string status = ServiceOrderStatus.Draft)
    {
        var order = new ServiceOrder
        {
            Id = id,
            Title = id,
            OrderTypeId = orderTypeId,
            Status = status,
            CreatedBy = "operator-1",
            OrganizationId = organizationId ?? harness.TenantId,
            ParentOrderId = parentOrderId,
            CreatedAt = CreatedAt,
        };
        return harness.Orders.AddAsync(order);
    }

    /// <summary>Moves an order along <paramref name="statuses"/>, one legal transition at a time.</summary>
    private static async Task MoveOrderAsync(TargetStoreContractHarness harness, string orderId, params string[] statuses)
    {
        foreach (var status in statuses)
        {
            var order = (ServiceOrder)(await harness.Orders.GetByIdAsync(orderId))!;
            order.Status = status;
            await harness.Orders.UpdateAsync(order);
        }
    }

    private static ServiceOrderTargetBuilder Target(TargetStoreContractHarness harness, string orderId, string featureId) =>
        new ServiceOrderTargetBuilder()
            .WithId($"{orderId}:{featureId}")
            .WithOrderId(orderId)
            .WithFeatureId(featureId)
            .WithAlias(null)
            .WithOrganizationId(harness.TenantId);

    private static async Task<IReadOnlyList<ServiceOrderTarget>> AllTargetsAsync(TargetStoreContractHarness harness, string orderId) =>
        (await harness.Reader.GetPageAsync(new OrderTargetQuery(orderId, pageSize: OrderTargetQuery.MaxPageSize), CancellationToken.None)).Items;

    // ── AddRangeAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task AddRangeAsync_NewTargets_ReturnsHowManyWereInsertedAndStoresThemAsGiven()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        var full = Target(harness, "ord-1", "feat-1")
            .WithId("tgt-full")
            .WithAssetTypeId("pole")
            .WithAlias("P-0001")
            .WithActivityIds("inspect", "replace", "clean")
            .WithStatus(OrderTargetStatus.InProgress)
            .WithConflictLevel(OrderTargetConflictLevel.Warning)
            .WithNote("north side of the street")
            .WithPosition(-38.501234, -12.970987)
            .WithAddedAt(new DateTime(2026, 4, 1, 8, 30, 15, DateTimeKind.Utc))
            .WithAddedBy("operator-7")
            .WithVersion(4)
            .Build();
        var bare = Target(harness, "ord-1", "feat-2").WithId("tgt-bare").WithoutPosition().Build();

        var inserted = await harness.Writer.AddRangeAsync("ord-1", [full, bare], CancellationToken.None);

        inserted.Should().Be(2);
        var stored = await AllTargetsAsync(harness, "ord-1");
        stored.Should().BeEquivalentTo([full, bare], options => options.WithStrictOrdering());
        stored[0].ActivityIds.Should().Equal("inspect", "replace", "clean");
        stored[0].Version.Should().Be(4, "an inserted target keeps the version it was given");
        stored[1].Version.Should().Be(0);
    }

    [Fact]
    public async Task AddRangeAsync_FeatureTheOrderAlreadyHolds_IsSkippedAndTheStoredTargetIsNotChanged()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        var first = Target(harness, "ord-1", "feat-1").WithId("tgt-first").WithAlias("P-1").WithNote("kept").Build();
        await harness.Writer.AddRangeAsync("ord-1", [first], CancellationToken.None);

        var again = Target(harness, "ord-1", "feat-1").WithId("tgt-second").WithAlias("other").WithNote("ignored").Build();
        var inserted = await harness.Writer.AddRangeAsync("ord-1", [again], CancellationToken.None);

        inserted.Should().Be(0);
        (await AllTargetsAsync(harness, "ord-1")).Should().BeEquivalentTo([first]);
    }

    [Fact]
    public async Task AddRangeAsync_RepeatingTheSameCall_ReturnsZeroTheSecondTime()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        ServiceOrderTarget[] targets = [Target(harness, "ord-1", "feat-1").Build(), Target(harness, "ord-1", "feat-2").Build()];

        var first = await harness.Writer.AddRangeAsync("ord-1", targets, CancellationToken.None);
        var second = await harness.Writer.AddRangeAsync("ord-1", targets, CancellationToken.None);

        first.Should().Be(2);
        second.Should().Be(0);
        (await harness.Reader.CountAsync("ord-1", CancellationToken.None)).Should().Be(2);
    }

    [Fact]
    public async Task AddRangeAsync_BatchMixingHeldAndNewFeatures_InsertsOnlyTheNewOnes()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await harness.Writer.AddRangeAsync("ord-1", [Target(harness, "ord-1", "feat-1").Build()], CancellationToken.None);

        var inserted = await harness.Writer.AddRangeAsync(
            "ord-1",
            [
                Target(harness, "ord-1", "feat-1").WithId("tgt-dup").Build(),
                Target(harness, "ord-1", "feat-2").Build(),
                Target(harness, "ord-1", "feat-3").Build(),
            ],
            CancellationToken.None);

        inserted.Should().Be(2);
        (await AllTargetsAsync(harness, "ord-1")).Select(t => t.Id).Should().Equal("ord-1:feat-1", "ord-1:feat-2", "ord-1:feat-3");
    }

    [Fact]
    public async Task AddRangeAsync_SameFeatureTwiceInOneCall_StoresTheFirstOnly()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        var first = Target(harness, "ord-1", "feat-1").WithId("tgt-first").WithNote("first").Build();
        var second = Target(harness, "ord-1", "feat-1").WithId("tgt-second").WithNote("second").Build();

        var inserted = await harness.Writer.AddRangeAsync("ord-1", [first, second], CancellationToken.None);

        inserted.Should().Be(1);
        (await AllTargetsAsync(harness, "ord-1")).Should().BeEquivalentTo([first]);
    }

    [Fact]
    public async Task AddRangeAsync_SameFeatureInTwoOrders_IsStoredForBoth()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await SeedOrderAsync(harness, "ord-2");

        var first = await harness.Writer.AddRangeAsync("ord-1", [Target(harness, "ord-1", "feat-1").Build()], CancellationToken.None);
        var second = await harness.Writer.AddRangeAsync("ord-2", [Target(harness, "ord-2", "feat-1").Build()], CancellationToken.None);

        first.Should().Be(1);
        second.Should().Be(1);
        (await harness.Reader.CountAsync("ord-1", CancellationToken.None)).Should().Be(1);
        (await harness.Reader.CountAsync("ord-2", CancellationToken.None)).Should().Be(1);
    }

    [Fact]
    public async Task AddRangeAsync_MoreTargetsThanOneInsertStatementCarries_InsertsThemAll()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        var targets = Enumerable.Range(0, 250)
            .Select(i => Target(harness, "ord-1", $"feat-{i:D4}").Build())
            .ToList();

        var inserted = await harness.Writer.AddRangeAsync("ord-1", targets, CancellationToken.None);
        var repeated = await harness.Writer.AddRangeAsync("ord-1", targets, CancellationToken.None);

        inserted.Should().Be(250);
        repeated.Should().Be(0);
        (await harness.Reader.CountAsync("ord-1", CancellationToken.None)).Should().Be(250);
    }

    [Fact]
    public async Task AddRangeAsync_EmptyTargets_ReturnsZeroEvenForAnOrderThatDoesNotExist()
    {
        await using var harness = await CreateHarnessAsync();

        var inserted = await harness.Writer.AddRangeAsync("ord-missing", [], CancellationToken.None);

        inserted.Should().Be(0);
    }

    [Fact]
    public async Task AddRangeAsync_BatchContainingAnotherOrdersTarget_ThrowsArgumentExceptionAndInsertsNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await SeedOrderAsync(harness, "ord-2");

        var act = () => harness.Writer.AddRangeAsync(
            "ord-1",
            [Target(harness, "ord-1", "feat-1").Build(), Target(harness, "ord-2", "feat-2").Build()],
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        (await harness.Reader.CountAsync("ord-1", CancellationToken.None)).Should().Be(0);
        (await harness.Reader.CountAsync("ord-2", CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task AddRangeAsync_OrderThatDoesNotExist_ThrowsKeyNotFoundExceptionAndInsertsNothing()
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => harness.Writer.AddRangeAsync(
            "ord-missing", [Target(harness, "ord-missing", "feat-1").Build()], CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await harness.Reader.CountAsync("ord-missing", CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task AddRangeAsync_TargetCarryingAnotherOrganizationThanItsOrder_ThrowsArgumentExceptionAndInsertsNothing()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");

        var act = () => harness.Writer.AddRangeAsync(
            "ord-1",
            [
                Target(harness, "ord-1", "feat-1").Build(),
                Target(harness, "ord-1", "feat-2").WithOrganizationId(OtherOrganizationId).Build(),
            ],
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        (await harness.Reader.CountAsync("ord-1", CancellationToken.None)).Should().Be(0);
    }

    [Fact]
    public async Task AddRangeAsync_NullTargets_ThrowsArgumentNullException()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");

        var act = () => harness.Writer.AddRangeAsync("ord-1", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AddRangeAsync_BlankOrderId_ThrowsArgumentException(string orderId)
    {
        await using var harness = await CreateHarnessAsync();

        var act = () => harness.Writer.AddRangeAsync(orderId, [], CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AddRangeAsync_AfterRemovingTheTarget_TheFeatureCanBeAddedAgain()
    {
        await using var harness = await CreateHarnessAsync();
        await SeedOrderAsync(harness, "ord-1");
        await harness.Writer.AddRangeAsync("ord-1", [Target(harness, "ord-1", "feat-1").WithId("tgt-old").Build()], CancellationToken.None);
        await harness.Writer.RemoveAsync("ord-1", ["tgt-old"], CancellationToken.None);

        var inserted = await harness.Writer.AddRangeAsync(
            "ord-1", [Target(harness, "ord-1", "feat-1").WithId("tgt-new").Build()], CancellationToken.None);

        inserted.Should().Be(1);
        (await AllTargetsAsync(harness, "ord-1")).Select(t => t.Id).Should().Equal("tgt-new");
    }
}
