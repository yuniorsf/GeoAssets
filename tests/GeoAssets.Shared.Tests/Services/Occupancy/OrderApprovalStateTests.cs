using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Shared.Services.Occupancy;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using GeoAssets.Workflow.Targets;
using Xunit;

namespace GeoAssets.Shared.Tests.Services.Occupancy;

/// <summary>
/// When the order detail offers the approval that commits staged occupancy, and what it says
/// when the approval fails (XD01-318) — including that every key exists in the three languages.
/// </summary>
public sealed class OrderApprovalStateTests
{
    private static readonly Guid Owner = new("a0a0a0a0-0000-4000-8000-000000000318");
    private static readonly OrderTypeRegistry Registry = NewRegistry();
    private static readonly ServiceOrderRules Rules = new(Registry);
    private static readonly WorkflowPrincipal OwnerSupervisor = new("sup-1", Owner.ToString(), ["Supervisor"], [], []);

    private static OrderTypeRegistry NewRegistry()
    {
        var registry = new OrderTypeRegistry();
        SharedUseOrderTypes.RegisterAll(registry, ["pole"]);
        registry.Register(new OrderType { Id = "plain", DisplayName = "Plain" });
        return registry;
    }

    private static ServiceOrder Order(string status = ServiceOrderStatus.Completed, string orderTypeId = SharedUseOrderTypes.PoleWorkId) => new()
    {
        Id = "ord-1",
        Title = "Pole work",
        OrderTypeId = orderTypeId,
        Status = status,
        CreatedBy = "planner-1",
        OrganizationId = Owner,
    };

    [Fact]
    public void CanApprove_ACompletedPoleWorkOrder_ForASupervisorOfItsOrganization()
    {
        OrderApprovalState.CanApprove(Registry.Find(SharedUseOrderTypes.PoleWorkId), Order(), OwnerSupervisor, Rules).Should().BeTrue();
    }

    [Fact]
    public void CanApprove_IsFalse_ForATypeThatDoesNotCommitOccupancy()
    {
        OrderApprovalState.CanApprove(Registry.Find("plain"), Order(orderTypeId: "plain"), OwnerSupervisor, Rules).Should().BeFalse();
        OrderApprovalState.CanApprove(null, Order(), OwnerSupervisor, Rules).Should().BeFalse();
    }

    // XD01-361: ServiceOrderDetail gates its occupancy panel (HasTargets) and the approve button (CanApprove) on the same
    // OccupancyApproval.Commits; the project has no component-rendering harness, so the gate is proven where it is exposed.
    [Fact]
    public void CanApprove_IsFalse_ForATargetedTypeWithoutTheOccupancyCommitCapability()
    {
        var poleWork = Registry.Find(SharedUseOrderTypes.PoleWorkId)!;
        var withoutCapability = new OrderType
        {
            Id = poleWork.Id,
            DisplayName = poleWork.DisplayName,
            States = poleWork.States,
            Transitions = poleWork.Transitions,
            TargetPolicy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"] },
        };

        OccupancyApproval.Commits(withoutCapability).Should().BeFalse("the occupancy panel is hidden with it");
        OrderApprovalState.CanApprove(withoutCapability, Order(), OwnerSupervisor, Rules).Should().BeFalse();
    }

    [Fact]
    public void CanApprove_IsTrue_ForTheSharedUseTypeThatDeclaresTheOccupancyCommitCapability()
    {
        var poleWork = Registry.Find(SharedUseOrderTypes.PoleWorkId)!;

        OccupancyApproval.Commits(poleWork).Should().BeTrue("the occupancy panel is shown with it");
        OrderApprovalState.CanApprove(poleWork, Order(), OwnerSupervisor, Rules).Should().BeTrue();
    }

    [Theory]
    [InlineData(ServiceOrderStatus.InProgress)]
    [InlineData(SharedUseOrderTypes.ApprovedStateKey)]
    public void CanApprove_IsFalse_FromAStatusWithNoApproval(string status)
    {
        OrderApprovalState.CanApprove(Registry.Find(SharedUseOrderTypes.PoleWorkId), Order(status), OwnerSupervisor, Rules).Should().BeFalse();
    }

    [Fact]
    public void CanApprove_IsFalse_ForACallerOfAnotherOrganizationOrOfNone()
    {
        var occupantOrganization = Guid.NewGuid();
        var occupant = new WorkflowPrincipal("occ-1", occupantOrganization.ToString(), ["Supervisor"], [], []);
        var noOrganization = new WorkflowPrincipal("x-1", null, ["Administrator"], [], []);
        var dispatched = Order();
        dispatched.Dispatches.Add(new OrderDispatch(occupantOrganization.ToString("D"), DispatchTargetType.Organization, "planner-1", DateTime.UnixEpoch));
        Rules.Evaluate(occupant, OrderActionType.Approve, dispatched).Code.Should().Be(
            RuleDenialCode.DeniedByRecipientAccess, "XD01-333: the pole-work type only informs a recipient, whatever its supervisor role grants");

        OrderApprovalState.CanApprove(Registry.Find(SharedUseOrderTypes.PoleWorkId), dispatched, occupant, Rules).Should().BeFalse();
        OrderApprovalState.CanApprove(Registry.Find(SharedUseOrderTypes.PoleWorkId), Order(), noOrganization, Rules).Should().BeFalse();
    }

    [Fact]
    public void CanApprove_IsFalse_ForAMemberTheRulesRefuse()
    {
        var crew = new WorkflowPrincipal("crew-1", Owner.ToString(), [], [], []);

        OrderApprovalState.CanApprove(Registry.Find(SharedUseOrderTypes.PoleWorkId), Order(), crew, Rules).Should().BeFalse();
    }

    [Fact]
    public void CanApprove_NullArguments_ThrowArgumentNullException()
    {
        var type = Registry.Find(SharedUseOrderTypes.PoleWorkId);

        FluentActions.Invoking(() => OrderApprovalState.CanApprove(type, null!, OwnerSupervisor, Rules)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => OrderApprovalState.CanApprove(type, Order(), null!, Rules)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => OrderApprovalState.CanApprove(type, Order(), OwnerSupervisor, null!)).Should().Throw<ArgumentNullException>();
    }

    public static TheoryData<Exception, string> Failures => new()
    {
        { new OccupancyConflictException(["pole-1"]), "orderApproval.error.conflict" },
        { new ServiceOrderConcurrencyException("ord-1"), "orderApproval.error.concurrency" },
        { new InvalidServiceOrderTransitionException("Approved", "Approved"), "orderApproval.error.invalidState" },
        { new OrderActionDeniedException("ord-1", RuleEvaluationResult.Deny(OrderActionType.Approve, "no")), "orderApproval.error.denied" },
        { new KeyNotFoundException("gone"), "orderApproval.error.notFound" },
        { new HttpRequestException("500"), "orderApproval.error.unexpected" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void ErrorKey_IsTheMostSpecificKeyForTheFailure(Exception failure, string expected)
    {
        OrderApprovalState.ErrorKey(failure).Should().Be(expected);
        OrderApprovalState.Keys.Should().Contain(expected);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("pt")]
    public void EveryKey_ExistsWithANonBlankValue_AndNoOtherApprovalKeyExists(string language)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;
        directory.Should().NotBeNull("the test runs from inside the repository");
        var path = Path.Combine(directory!.FullName, "apps", "GeoAssets.Shared", "wwwroot", "i18n", $"{language}.json");
        var strings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!;

        foreach (var key in OrderApprovalState.Keys)
            strings.Should().ContainKey(key).WhoseValue.Should().NotBeNullOrWhiteSpace($"'{key}' must be translated in {language}.json");
        strings.Keys.Where(k => k.StartsWith("orderApproval.", StringComparison.Ordinal)).Should().BeEquivalentTo(OrderApprovalState.Keys);
    }
}
