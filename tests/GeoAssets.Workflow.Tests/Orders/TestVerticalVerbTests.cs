using System.Text.Json;
using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Xunit;

namespace GeoAssets.Workflow.Tests.Orders;

/// <summary>
/// XD01-366, AC (c): a vertical defines a new verb (<see cref="SurveyActions.Survey"/>, declared in this test assembly) and the
/// engine — order types, rules, transitions, handler routing, the action log's JSON — carries it with no change to
/// <c>GeoAssets.Workflow</c>.
/// </summary>
public class TestVerticalVerbTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string TypeId = "survey-visit";
    private const string Permission = "survey:run";

    private static readonly OrderType SurveyType = new()
    {
        Id = TypeId,
        DisplayName = "Survey visit",
        ActionPermissions = [new(SurveyActions.Survey, PolicyKind.Permission, Permission)],
        States = [new("Pending", "Pending", IsSuccess: false), new("Surveyed", "Surveyed", IsSuccess: true)],
        Transitions = [new("Pending", "Surveyed", SurveyActions.Survey)],
        RecipientActions = [SurveyActions.Survey],
    };

    private static WorkflowPrincipal Principal(params string[] permissions) =>
        new("u1", null, [], [], permissions);

    private static ServiceOrder Order() => new() { OrderTypeId = TypeId, Status = "Pending" };

    private static ServiceOrderRules Rules()
    {
        var registry = new OrderTypeRegistry();
        registry.Register(SurveyType);
        return new ServiceOrderRules(registry);
    }

    private sealed class SurveyHandler : IOrderActionHandler
    {
        public bool CanHandle(OrderType? orderType, OrderActionLog entry) =>
            string.Equals(orderType?.Id, TypeId, StringComparison.Ordinal) && entry.Action == SurveyActions.Survey;

        public Task<OrderActionOutcome> HandleAsync(OrderActionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(OrderActionOutcome.Applied);
    }

    private sealed class AlsoSurveyHandler : IOrderActionHandler
    {
        public bool CanHandle(OrderType? orderType, OrderActionLog entry) => entry.Action == SurveyActions.Survey;

        public Task<OrderActionOutcome> HandleAsync(OrderActionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(OrderActionOutcome.Applied);
    }

    [Fact]
    public void Verb_IsNotAHistoricalAction()
    {
        OrderActionAliases.NumberOf(SurveyActions.Survey).Should().BeNull();
        OrderAction.Generic.Should().NotContain(SurveyActions.Survey);
        OrderActionAliases.All.Select(a => a.Action).Should().NotContain(SurveyActions.Survey);
    }

    [Fact]
    public void OrderType_DeclaresTheVerb_InATransition_APermission_AndRecipientActions()
    {
        SurveyType.Transitions.Single().TriggerAction.Should().Be(SurveyActions.Survey);
        SurveyType.ActionPermissions.Single().Action.Should().Be(SurveyActions.Survey);
        SurveyType.RecipientActions.Should().Equal(SurveyActions.Survey);
    }

    [Fact]
    public void Rules_PermissionSatisfied_Allows() =>
        Rules().Evaluate(Principal(Permission), SurveyActions.Survey, Order()).Allowed.Should().BeTrue();

    [Fact]
    public void Rules_PermissionNotSatisfied_IsDeniedByTheActionPermissionRule()
    {
        var verdict = Rules().Evaluate(Principal(), SurveyActions.Survey, Order());

        verdict.Allowed.Should().BeFalse();
        verdict.Action.Should().Be(SurveyActions.Survey);
        verdict.Reason.Should().Be("Denied by rule 'OrderTypeActionPermissionRule'.");
    }

    [Fact]
    public void Transitions_TransitionsFor_FindsTheVerbsTransition()
    {
        ServiceOrderTransitions.TransitionsFor(SurveyType, SurveyActions.Survey).Should().ContainSingle()
            .Which.Should().Be(new WorkflowTransition("Pending", "Surveyed", SurveyActions.Survey));
        ServiceOrderTransitions.TransitionsFor(SurveyType, OrderAction.Approve).Should().BeEmpty();
        ServiceOrderTransitions.HasTransitionFor(SurveyType, "Pending", SurveyActions.Survey).Should().BeTrue();
        ServiceOrderTransitions.HasTransitionFor(SurveyType, "Surveyed", SurveyActions.Survey).Should().BeFalse();
    }

    [Fact]
    public void Handlers_RouteTheVerbToItsHandler()
    {
        var handler = new SurveyHandler();
        var registry = new OrderActionHandlerRegistry([handler]);

        registry.Find(SurveyType, new OrderActionLog(SurveyActions.Survey, "u", DateTime.UnixEpoch)).Should().BeSameAs(handler);
        registry.Find(SurveyType, new OrderActionLog(OrderAction.Approve, "u", DateTime.UnixEpoch)).Should().BeNull();
    }

    [Fact]
    public void Handlers_EnsureUnambiguous_DetectsTwoHandlersClaimingTheVerbOnTheType()
    {
        var registry = new OrderActionHandlerRegistry([new SurveyHandler(), new AlsoSurveyHandler()]);

        var act = () => registry.EnsureUnambiguous([SurveyType]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{typeof(SurveyHandler).FullName}*{typeof(AlsoSurveyHandler).FullName}*Survey*{TypeId}*");
    }

    [Fact]
    public void Handlers_EnsureUnambiguous_ATypeThatNeverNamesTheVerb_IsNotProbedWithIt()
    {
        var registry = new OrderActionHandlerRegistry([new SurveyHandler(), new AlsoSurveyHandler()]);

        var act = () => registry.EnsureUnambiguous([new OrderType { Id = "plain", DisplayName = "Plain" }]);

        act.Should().NotThrow("a type that never names the verb is never probed with it — only the generic and historical verbs are");
    }

    [Fact]
    public void ActionLog_WithTheVerb_RoundTripsThroughJsonAsAString()
    {
        var entry = new OrderActionLog(SurveyActions.Survey, "u", new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc), "walked the route", "Surveyed");

        var json = JsonSerializer.Serialize(entry, Web);

        json.Should().Contain("\"action\":\"Survey\"");
        JsonSerializer.Deserialize<OrderActionLog>(json, Web).Should().Be(entry);
    }
}
