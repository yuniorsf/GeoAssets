using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using GeoAssets.Workflow.Targets;
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

    // ── AttributesSchemaJson (XD01-304, finding B3) ───────────────────────────

    private const string AttributesSchema =
        """{"type":"object","required":["crewSize"],"properties":{"crewSize":{"type":"integer","minimum":1}}}""";

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsAttributesSchemaJson()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);

        await repo.AddAsync(new OrderType { Id = "inspection", DisplayName = "Inspection", AttributesSchemaJson = AttributesSchema });
        await repo.SaveChangesAsync();

        using var freshContext = fixture.NewContext();
        var loaded = await new EFOrderTypeRepository(freshContext).GetByIdAsync("inspection");

        loaded!.AttributesSchemaJson.Should().Be(AttributesSchema);
    }

    [Fact]
    public async Task AddAsync_WithoutAttributesSchemaJson_LoadsNull()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);

        await repo.AddAsync(FullOrderType());
        await repo.SaveChangesAsync();

        using var freshContext = fixture.NewContext();
        var loaded = await new EFOrderTypeRepository(freshContext).GetByIdAsync("emergency-repair");

        loaded!.AttributesSchemaJson.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_ReplacesThenClearsAttributesSchemaJson()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);
        await repo.AddAsync(new OrderType { Id = "inspection", DisplayName = "Inspection" });
        await repo.SaveChangesAsync();

        await repo.UpdateAsync(new OrderType { Id = "inspection", DisplayName = "Inspection", AttributesSchemaJson = AttributesSchema });
        await repo.SaveChangesAsync();
        (await repo.GetByIdAsync("inspection"))!.AttributesSchemaJson.Should().Be(AttributesSchema);

        await repo.UpdateAsync(new OrderType { Id = "inspection", DisplayName = "Inspection" });
        await repo.SaveChangesAsync();

        using var freshContext = fixture.NewContext();
        (await new EFOrderTypeRepository(freshContext).GetByIdAsync("inspection"))!.AttributesSchemaJson.Should().BeNull();
    }

    // ── RecipientAccess (XD01-333) ────────────────────────────────────────────

    [Fact]
    public async Task RecipientAccess_IsAdded_Updated_AndReadBackFromAFreshContext()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);
        await repo.AddAsync(new OrderType { Id = "notice", DisplayName = "Notice", RecipientAccess = OrderRecipientAccess.Execute });
        await repo.AddAsync(new OrderType { Id = "plain", DisplayName = "Plain" });
        await repo.SaveChangesAsync();

        using (var freshContext = fixture.NewContext())
        {
            var reader = new EFOrderTypeRepository(freshContext);
            (await reader.GetByIdAsync("notice"))!.RecipientAccess.Should().Be(OrderRecipientAccess.Execute);
            (await reader.GetByIdAsync("plain"))!.RecipientAccess.Should().Be(OrderRecipientAccess.Inform);
        }

        await repo.UpdateAsync(new OrderType { Id = "notice", DisplayName = "Notice", RecipientAccess = OrderRecipientAccess.Edit });
        await repo.SaveChangesAsync();

        using var afterUpdate = fixture.NewContext();
        (await new EFOrderTypeRepository(afterUpdate).GetByIdAsync("notice"))!.RecipientAccess.Should().Be(OrderRecipientAccess.Edit);
    }

    // ── TargetPolicy (XD01-304) ───────────────────────────────────────────────

    private static OrderTargetPolicy FullTargetPolicy() => new()
    {
        TargetAssetTypeIds = ["pole", "manhole"],
        AliasAttributeKey = "tag",
        SelectionLimit = 500,
        Activities = [new("inspect", "Inspect"), new("replace", "Replace")],
        Exclusivity = OrderTargetExclusivity.BlockOnOverlap,
        FanOut = new OrderTargetFanOutPolicy("pole-inspection-notice", OrderTargetFanOutPartyKind.OccupantOrganization),
    };

    private static OrderType TypeWithPolicy(OrderTargetPolicy? policy) => new()
    {
        Id = "pole-inspection",
        DisplayName = "Pole inspection",
        TargetPolicy = policy,
    };

    [Fact]
    public async Task AddAsync_ThenGetById_RoundTripsFullTargetPolicy()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);

        await repo.AddAsync(TypeWithPolicy(FullTargetPolicy()));
        await repo.SaveChangesAsync();

        using var freshContext = fixture.NewContext();
        var loaded = await new EFOrderTypeRepository(freshContext).GetByIdAsync("pole-inspection");

        var policy = loaded!.TargetPolicy;
        policy.Should().NotBeNull();
        policy!.TargetAssetTypeIds.Should().Equal("pole", "manhole");
        policy.AliasAttributeKey.Should().Be("tag");
        policy.SelectionLimit.Should().Be(500);
        policy.Activities.Should().Equal(new OrderTargetActivity("inspect", "Inspect"), new OrderTargetActivity("replace", "Replace"));
        policy.Exclusivity.Should().Be(OrderTargetExclusivity.BlockOnOverlap);
        policy.FanOut.Should().Be(new OrderTargetFanOutPolicy("pole-inspection-notice", OrderTargetFanOutPartyKind.OccupantOrganization));
    }

    [Fact]
    public async Task GetAllAsync_ReturnsTargetPolicyAndAttributesSchemaJson()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);
        await repo.AddAsync(new OrderType
        {
            Id = "pole-inspection",
            DisplayName = "Pole inspection",
            AttributesSchemaJson = AttributesSchema,
            TargetPolicy = FullTargetPolicy(),
        });
        await repo.SaveChangesAsync();

        using var freshContext = fixture.NewContext();
        var loaded = (await new EFOrderTypeRepository(freshContext).GetAllAsync()).Single();

        loaded.AttributesSchemaJson.Should().Be(AttributesSchema);
        loaded.TargetPolicy.Should().BeEquivalentTo(FullTargetPolicy());
    }

    [Fact]
    public async Task AddAsync_NullTargetPolicy_StoresNullColumnAndLoadsNull()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);

        await repo.AddAsync(TypeWithPolicy(policy: null));
        await repo.SaveChangesAsync();

        using var freshContext = fixture.NewContext();
        freshContext.OrderTypes.Single().TargetPolicyJson.Should().BeNull();
        (await new EFOrderTypeRepository(freshContext).GetByIdAsync("pole-inspection"))!.TargetPolicy.Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_AddsReplacesThenClearsTargetPolicy()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);
        await repo.AddAsync(TypeWithPolicy(policy: null));
        await repo.SaveChangesAsync();

        await repo.UpdateAsync(TypeWithPolicy(FullTargetPolicy()));
        await repo.SaveChangesAsync();
        (await repo.GetByIdAsync("pole-inspection"))!.TargetPolicy.Should().BeEquivalentTo(FullTargetPolicy());

        var narrowed = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"], Exclusivity = OrderTargetExclusivity.WarnOnOverlap };
        await repo.UpdateAsync(TypeWithPolicy(narrowed));
        await repo.SaveChangesAsync();
        (await repo.GetByIdAsync("pole-inspection"))!.TargetPolicy.Should().BeEquivalentTo(narrowed);

        await repo.UpdateAsync(TypeWithPolicy(policy: null));
        await repo.SaveChangesAsync();

        using var freshContext = fixture.NewContext();
        (await new EFOrderTypeRepository(freshContext).GetByIdAsync("pole-inspection"))!.TargetPolicy.Should().BeNull();
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
