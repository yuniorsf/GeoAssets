using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Persistence;
using Xunit;

namespace GeoAssets.Workflow.EFCore.Tests;

/// <summary>
/// XD01-366 through the real <see cref="ServiceOrderDbContext"/> mapping: the action log and the order type tables keep each
/// historical action's number (so rows written before the code column read, and are written, as they always were), add its
/// code, and store a new verb as a code with a null number.
/// </summary>
public class OrderActionPersistenceTests
{
    private static readonly DateTime At = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    public static TheoryData<OrderActionType> HistoricalValues()
    {
        var data = new TheoryData<OrderActionType>();
        foreach (var value in Enum.GetValues<OrderActionType>())
        {
            data.Add(value);
        }

        return data;
    }

    private static ServiceOrder Order(string id) => new()
    {
        Id = id,
        OrderTypeId = "inspection",
        Status = ServiceOrderStatus.Draft,
        CreatedBy = "u1",
        CreatedAt = At,
    };

    [Fact]
    public async Task OrderActionPersistence_EveryHistoricalValue_RoundTrips()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("o-1"));
        var values = Enum.GetValues<OrderActionType>();
        foreach (var value in values)
        {
            await repo.AppendActionAsync("o-1", new OrderActionLog(value, "tech", At, $"entry {(int)value}"));
        }

        using var fresh = fixture.NewContext();
        var loaded = await new EFServiceOrderRepository(fresh).GetByIdAsync("o-1");

        loaded!.ActionLog.Select(e => e.Action).Should().Equal(values.Select(v => (OrderAction)v));
        loaded.ActionLog.Select(e => e.Comment).Should().Equal(values.Select(v => $"entry {(int)v}"));
        var rows = fresh.OrderActionLogs.OrderBy(r => r.Id).ToList();
        rows.Select(r => r.Action).Should().Equal(values.Select(v => (int?)(int)v));
        rows.Select(r => r.ActionCode).Should().Equal(values.Select(v => v.ToString()));
    }

    [Fact]
    public async Task OrderActionPersistence_NewVerb_StoresCodeAndNullNumber()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("o-1"));

        await repo.AppendActionAsync("o-1", new OrderActionLog(new OrderAction("Survey"), "tech", At, "walked it"));

        using var fresh = fixture.NewContext();
        var row = fresh.OrderActionLogs.Single();
        row.Action.Should().BeNull();
        row.ActionCode.Should().Be("Survey");
        (await new EFServiceOrderRepository(fresh).GetByIdAsync("o-1"))!.ActionLog
            .Should().ContainSingle().Which.Action.Should().Be(new OrderAction("Survey"));
    }

    [Fact]
    public async Task OrderActionPersistence_ARowWrittenBeforeTheCodeColumn_ReadsByItsNumber()
    {
        using var fixture = new SqliteFixture();
        var repo = new EFServiceOrderRepository(fixture.Context);
        await repo.AddAsync(Order("o-1"));
        fixture.Context.OrderActionLogs.Add(new()
        {
            ServiceOrderId = "o-1", Action = 13, ActionCode = null, PerformedBy = "u", PerformedAt = At, ActorKind = 0,
        });
        await fixture.Context.SaveChangesAsync();

        using var fresh = fixture.NewContext();

        (await new EFServiceOrderRepository(fresh).GetByIdAsync("o-1"))!.ActionLog
            .Should().ContainSingle().Which.Action.Should().Be(OrderActionAliases.FromNumber(13));
    }

    [Fact]
    public async Task OrderTypePersistence_NewVerb_InPermissionsTransitionsAndRecipientActions_RoundTrips()
    {
        var survey = new OrderAction("Survey");
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);
        await repo.AddAsync(new OrderType
        {
            Id = "survey-visit",
            DisplayName = "Survey visit",
            InitialStateKey = "Pending",
            ActionPermissions =
            [
                new(survey, PolicyKind.Permission, "survey:run", FromStateKey: "Pending"),
                new(OrderAction.Approve, PolicyKind.Role, "Supervisor"),
            ],
            States = [new("Pending", "Pending"), new("Surveyed", "Surveyed", IsSuccess: true)],
            Transitions =
            [
                new("Pending", "Surveyed", survey),
                new("Surveyed", "Surveyed", OrderAction.Complete),
                new("Pending", "Pending"),
            ],
            RecipientActions = [survey, OrderAction.Annotate],
        });
        await repo.SaveChangesAsync();

        using var fresh = fixture.NewContext();
        var loaded = await new EFOrderTypeRepository(fresh).GetByIdAsync("survey-visit");

        loaded!.ActionPermissions.Select(p => p.Action).Should().Equal(survey, OrderAction.Approve);
        loaded.Transitions.Select(t => t.TriggerAction).Should().BeEquivalentTo(new OrderAction?[] { survey, OrderAction.Complete, null });
        loaded.RecipientActions.Should().Equal(survey, OrderAction.Annotate);

        fresh.ActionPermissions.OrderBy(p => p.Id).AsEnumerable().Select(p => (p.Action, p.ActionCode))
            .Should().Equal(((int?)null, (string?)"Survey"), ((int?)1, "Approve"));
        fresh.OrderTypeTransitions.AsEnumerable().Select(t => (t.TriggerAction, t.TriggerActionCode))
            .Should().BeEquivalentTo([((int?)null, (string?)"Survey"), ((int?)7, "Complete"), ((int?)null, (string?)null)]);
        fresh.OrderTypeRecipientActions.AsEnumerable().Select(a => (a.Action, a.ActionCode)).Distinct()
            .Should().BeEquivalentTo([((int?)null, (string?)"Survey"), ((int?)9, "Annotate")]);
    }

    [Theory]
    [MemberData(nameof(HistoricalValues))]
    public async Task OrderTypePersistence_EveryHistoricalValue_KeepsItsNumberInEachTable(OrderActionType value)
    {
        OrderAction action = value;
        using var fixture = new SqliteFixture();
        var repo = new EFOrderTypeRepository(fixture.Context);
        await repo.AddAsync(new OrderType
        {
            Id = "t",
            DisplayName = "T",
            ActionPermissions = [new(action, PolicyKind.Role, "R")],
            States = [new("A", "A"), new("B", "B")],
            Transitions = [new("A", "B", action)],
            RecipientActions = [action],
        });
        await repo.SaveChangesAsync();

        using var fresh = fixture.NewContext();
        var loaded = await new EFOrderTypeRepository(fresh).GetByIdAsync("t");

        loaded!.ActionPermissions.Single().Action.Should().Be(action);
        loaded.Transitions.Single().TriggerAction.Should().Be(action);
        loaded.RecipientActions.Should().Equal(action);
        fresh.ActionPermissions.Single().Action.Should().Be((int)value);
        fresh.ActionPermissions.Single().ActionCode.Should().Be(value.ToString());
        fresh.OrderTypeTransitions.Single().TriggerAction.Should().Be((int)value);
        fresh.OrderTypeTransitions.Single().TriggerActionCode.Should().Be(value.ToString());
        fresh.OrderTypeRecipientActions.AsEnumerable().Should().OnlyContain(a => a.Action == (int)value && a.ActionCode == value.ToString());
    }

    [Fact]
    public async Task OrderTypePersistence_RowsWrittenBeforeTheCodeColumns_ReadByNumber()
    {
        using var fixture = new SqliteFixture();
        fixture.Context.OrderTypes.Add(new() { Id = "old", DisplayName = "Old" });
        fixture.Context.ActionPermissions.Add(new() { OrderTypeId = "old", Action = 13, Kind = (int)PolicyKind.Role, Value = "R" });
        fixture.Context.OrderTypeTransitions.Add(new() { OrderTypeId = "old", FromStateKey = "A", ToStateKey = "B", TriggerAction = 12 });
        fixture.Context.OrderTypeRecipientActions.Add(new() { OrderTypeId = "old", Action = 13 });
        await fixture.Context.SaveChangesAsync();

        using var fresh = fixture.NewContext();
        var loaded = await new EFOrderTypeRepository(fresh).GetByIdAsync("old");

        loaded!.ActionPermissions.Single().Action.Code.Should().Be("Dispute");
        loaded.Transitions.Single().TriggerAction!.Value.Code.Should().Be("FlagClandestine");
        loaded.RecipientActions.Should().Equal(OrderActionAliases.FromNumber(13));
    }
}
