using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

public class EFOrderTypeRepositoryTests
{
    private static OrderType FullOrderType(string id = "emergency-repair") => new()
    {
        Id              = id,
        DisplayName     = "Emergency Repair",
        Description     = "Urgent field repair",
        InitialStateKey = "Intake",
        CreationPolicies =
        [
            new(PolicyKind.Role, "Supervisor"),
        ],
        ActionPermissions =
        [
            new(OrderActionType.Approve, PolicyKind.Permission, "emergency:approve", FromStateKey: "Pending"),
        ],
        States =
        [
            new("Intake", "Intake", IsSuccess: false),
            new("Resolved", "Resolved", IsSuccess: true),
        ],
        Transitions =
        [
            new("Intake", "Resolved", OrderActionType.Complete),
            new("Intake", "Intake"), // no TriggerAction — proves the null case round-trips too
        ],
    };

    // ── AddAsync / GetByIdAsync ─────────────────────────────────────────────────

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsAllFields()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);

        await repo.AddAsync(FullOrderType());
        await repo.SaveChangesAsync();

        var loaded = await repo.GetByIdAsync("emergency-repair");

        loaded.Should().NotBeNull();
        loaded!.DisplayName.Should().Be("Emergency Repair");
        loaded.Description.Should().Be("Urgent field repair");
        loaded.InitialStateKey.Should().Be("Intake");
        loaded.CreationPolicies.Should().ContainSingle(p => p.Kind == PolicyKind.Role && p.Value == "Supervisor");
        loaded.ActionPermissions.Should().ContainSingle(p =>
            p.Action == OrderActionType.Approve && p.Value == "emergency:approve" && p.FromStateKey == "Pending");
        loaded.States.Should().BeEquivalentTo(FullOrderType().States);
        loaded.Transitions.Should().BeEquivalentTo(FullOrderType().Transitions);
    }

    [Fact]
    public async Task AddAsync_WithoutSaveChanges_DoesNotPersist()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);

        await repo.AddAsync(FullOrderType());
        // Deliberately no SaveChangesAsync() call.

        using var freshContext = fixture.NewContext();
        var freshRepo = new EFOrderTypeRepository(freshContext);
        (await freshRepo.GetByIdAsync("emergency-repair")).Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);

        (await repo.GetByIdAsync("missing")).Should().BeNull();
    }

    [Fact]
    public async Task GetAllAsync_ReturnsAllOrderedByDisplayName()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);
        await repo.AddAsync(new OrderType { Id = "b", DisplayName = "Zebra" });
        await repo.AddAsync(new OrderType { Id = "a", DisplayName = "Alpha" });
        await repo.SaveChangesAsync();

        var all = await repo.GetAllAsync();

        all.Select(t => t.Id).Should().Equal("a", "b");
    }

    // ── UpdateAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAsync_ReplacesChildCollectionsEntirely()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);
        await repo.AddAsync(FullOrderType());
        await repo.SaveChangesAsync();

        var replacement = new OrderType
        {
            Id          = "emergency-repair",
            DisplayName = "Emergency Repair (renamed)",
            Description = "Updated description",
            CreationPolicies  = [new(PolicyKind.Role, "Administrator")],
            ActionPermissions = [],
            States      = [new("Draft", "Draft")],
            Transitions = [],
        };

        await repo.UpdateAsync(replacement);
        await repo.SaveChangesAsync();

        var loaded = await repo.GetByIdAsync("emergency-repair");
        loaded!.DisplayName.Should().Be("Emergency Repair (renamed)");
        loaded.InitialStateKey.Should().BeNull();
        loaded.CreationPolicies.Should().ContainSingle(p => p.Value == "Administrator");
        loaded.ActionPermissions.Should().BeEmpty();
        loaded.States.Should().ContainSingle(s => s.Key == "Draft");
        loaded.Transitions.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateAsync_UnknownId_IsNoOp()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);

        await repo.UpdateAsync(new OrderType { Id = "missing", DisplayName = "Missing" });
        await repo.SaveChangesAsync();

        (await repo.GetByIdAsync("missing")).Should().BeNull();
    }

    // ── DeleteAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAsync_RemovesOrderTypeAndCascadesChildren()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);
        await repo.AddAsync(FullOrderType());
        await repo.SaveChangesAsync();

        await repo.DeleteAsync("emergency-repair");
        await repo.SaveChangesAsync();

        (await repo.GetByIdAsync("emergency-repair")).Should().BeNull();
        fixture.Context.ActionPermissions.Count(p => p.OrderTypeId == "emergency-repair").Should().Be(0);
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_IsNoOp()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);

        var act = async () =>
        {
            await repo.DeleteAsync("missing");
            await repo.SaveChangesAsync();
        };

        await act.Should().NotThrowAsync();
    }
}
