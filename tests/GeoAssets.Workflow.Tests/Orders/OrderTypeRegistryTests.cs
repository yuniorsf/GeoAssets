using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

public class OrderTypeRegistryTests
{
    private static OrderTargetPolicy ValidPolicy(OrderTargetFanOutPolicy? fanOut = null) => new()
    {
        TargetAssetTypeIds = ["pole"],
        FanOut = fanOut,
    };

    private static OrderType TypeWith(string id, OrderTargetPolicy? policy, string displayName = "Type") => new()
    {
        Id = id,
        DisplayName = displayName,
        TargetPolicy = policy,
    };

    private sealed class StubOrderTypeRepository(params OrderType[] types) : IOrderTypeRepository
    {
        public Task<OrderType?> GetByIdAsync(string id, CancellationToken ct = default) =>
            Task.FromResult(types.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.OrdinalIgnoreCase)));

        public Task<IReadOnlyList<OrderType>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrderType>>(types);

        public Task AddAsync(OrderType orderType, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrderType orderType, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    // ── No policy: unchanged behaviour ────────────────────────────────────────

    [Fact]
    public void OrderType_TargetPolicy_DefaultsToNull()
    {
        new OrderType { Id = "inspection", DisplayName = "Inspection" }.TargetPolicy.Should().BeNull();
    }

    [Fact]
    public void Register_TypeWithoutTargetPolicy_IsStoredAndFoundCaseInsensitively()
    {
        var registry = new OrderTypeRegistry();
        var type = TypeWith("inspection", policy: null);

        registry.Register(type);

        registry.Find("INSPECTION").Should().BeSameAs(type);
        registry.Get("inspection").Should().BeSameAs(type);
        registry.All.Should().ContainSingle().Which.Should().BeSameAs(type);
    }

    [Fact]
    public void Register_SameIdTwice_ReplacesTheEarlierEntry()
    {
        var registry = new OrderTypeRegistry();
        var replacement = TypeWith("inspection", policy: null, displayName: "Replacement");

        registry.Register(TypeWith("inspection", policy: null));
        registry.Register(replacement);

        registry.All.Should().ContainSingle().Which.Should().BeSameAs(replacement);
    }

    [Fact]
    public void Register_Null_ThrowsArgumentNullException()
    {
        var act = () => new OrderTypeRegistry().Register(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Get_UnknownId_ThrowsKeyNotFoundException()
    {
        var act = () => new OrderTypeRegistry().Get("missing");

        act.Should().Throw<KeyNotFoundException>();
    }

    // ── Policy validated on Register ──────────────────────────────────────────

    [Fact]
    public void Register_TypeWithValidTargetPolicy_IsStoredWithItsPolicy()
    {
        var registry = new OrderTypeRegistry();
        var policy = ValidPolicy();

        registry.Register(TypeWith("pole-inspection", policy));

        registry.Get("pole-inspection").TargetPolicy.Should().BeSameAs(policy);
    }

    [Fact]
    public void Register_TypeWithInvalidTargetPolicy_ThrowsAndLeavesRegistryUntouched()
    {
        var registry = new OrderTypeRegistry();
        var existing = TypeWith("pole-inspection", policy: null);
        registry.Register(existing);

        var act = () => registry.Register(TypeWith("pole-inspection", ValidPolicy() with { TargetAssetTypeIds = [] }));

        act.Should().Throw<ArgumentException>();
        registry.All.Should().ContainSingle().Which.Should().BeSameAs(existing);
    }

    [Fact]
    public void Register_FanOutChildEqualsOwnId_Throws()
    {
        var registry = new OrderTypeRegistry();

        var act = () => registry.Register(
            TypeWith("pole-inspection", ValidPolicy(new OrderTargetFanOutPolicy("Pole-Inspection"))));

        act.Should().Throw<ArgumentException>().WithMessage("*cannot fan out into itself*");
        registry.All.Should().BeEmpty();
    }

    [Fact]
    public void Register_FanOutChildNotRegisteredYet_IsAccepted()
    {
        // Child existence is deferred: a parent may be registered before its child.
        var registry = new OrderTypeRegistry();

        registry.Register(TypeWith("parent", ValidPolicy(new OrderTargetFanOutPolicy("child"))));
        registry.Find("child").Should().BeNull();

        registry.Register(TypeWith("child", policy: null));
        registry.Find(registry.Get("parent").TargetPolicy!.FanOut!.ChildOrderTypeId).Should().NotBeNull();
    }

    // ── Policy validated on LoadFromAsync ─────────────────────────────────────

    [Fact]
    public async Task LoadFromAsync_TypesWithAndWithoutPolicy_AreAllLoaded()
    {
        var registry = new OrderTypeRegistry();
        var repository = new StubOrderTypeRepository(
            TypeWith("parent", ValidPolicy(new OrderTargetFanOutPolicy("child"))),
            TypeWith("child", policy: null));

        await registry.LoadFromAsync(repository);

        registry.All.Select(t => t.Id).Should().BeEquivalentTo("parent", "child");
        registry.Get("parent").TargetPolicy!.FanOut!.ChildOrderTypeId.Should().Be("child");
    }

    [Fact]
    public async Task LoadFromAsync_OneTypeHasInvalidPolicy_ThrowsAndLoadsNothing()
    {
        var registry = new OrderTypeRegistry();
        var existing = TypeWith("valid", policy: null, displayName: "Existing");
        registry.Register(existing);
        var repository = new StubOrderTypeRepository(
            TypeWith("valid", policy: null, displayName: "From repository"),
            TypeWith("broken", ValidPolicy() with { SelectionLimit = 0 }));

        var act = () => registry.LoadFromAsync(repository);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
        registry.All.Should().ContainSingle().Which.Should().BeSameAs(existing);
    }
}
