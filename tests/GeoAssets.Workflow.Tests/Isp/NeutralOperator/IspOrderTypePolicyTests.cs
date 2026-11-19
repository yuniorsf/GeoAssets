using FluentAssertions;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Isp.NeutralOperator;

/// <summary>
/// Who may act on the ISP order types is data (XD01-364): the clandestine notice and the tenant's attachment request
/// declare it, and <see cref="ServiceOrderRules"/> evaluates it — the one decision point the server's routes use.
/// </summary>
public sealed class IspOrderTypePolicyTests
{
    private static readonly Guid Owner = new("a0a0a0a0-0000-4000-8000-000000000364");
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);

    private static OrderTypeRegistry Registry()
    {
        var registry = new OrderTypeRegistry();
        ClandestineAttachmentOrderTypes.RegisterAll(registry);
        TenantAttachmentRequestOrderTypes.RegisterAll(registry);
        return registry;
    }

    private static ServiceOrderRules Rules() => new(Registry(), timeProvider: new FakeTimeProvider(Now));

    private static WorkflowPrincipal Member(params string[] roles) => new("user-1", Owner.ToString(), roles, [], []);

    private static ServiceOrder Notice(string status = ClandestineAttachmentWorkflowGraph.CaboARevelia) => new()
    {
        Id = "notice-1",
        OrderTypeId = ClandestineAttachmentOrderTypes.ClandestineNoticeId,
        Status = status,
        OrganizationId = Owner,
        CreatedBy = "someone-else",
    };

    // ── What the types declare ────────────────────────────────────────────────

    [Fact]
    public void ClandestineNotice_DeclaresWhoMayDispute_AndWhatItsRecipientsMayDo()
    {
        var notice = ClandestineAttachmentOrderTypes.CreateClandestineNotice();

        notice.ActionPermissions.Where(p => p.Action == OrderActionType.Dispute)
            .Should().ContainSingle().Which.Should().Be(new OrderActionPermission(OrderActionType.Dispute, PolicyKind.Role, IspRoles.Tenant));
        notice.RecipientActions.Should().Equal(OrderActionType.Dispute);
        notice.RecipientAccess.Should().Be(OrderRecipientAccess.Inform);
        notice.CreationPolicies.Should().BeEquivalentTo(
            [new OrderCreationPolicy(PolicyKind.Role, IspRoles.Supervisor), new OrderCreationPolicy(PolicyKind.Role, IspRoles.Administrator)]);
    }

    [Theory]
    [InlineData(OrderActionType.FlagClandestine)]
    [InlineData(OrderActionType.Approve)]
    [InlineData(OrderActionType.Reject)]
    public void ClandestineNotice_StaffActions_AreOpenToSupervisorAndAdministratorOnly(OrderActionType action) =>
        ClandestineAttachmentOrderTypes.CreateClandestineNotice().ActionPermissions.Where(p => p.Action == action)
            .Select(p => (p.Kind, p.Value))
            .Should().BeEquivalentTo([(PolicyKind.Role, IspRoles.Supervisor), (PolicyKind.Role, IspRoles.Administrator)]);

    [Fact]
    public void AttachmentRequest_DeclaresIspTenantAsItsOnlyCreationPolicy_AndInformsItsRecipients()
    {
        var request = TenantAttachmentRequestOrderTypes.CreateAttachmentRequest();

        request.CreationPolicies.Should().ContainSingle().Which.Should().Be(new OrderCreationPolicy(PolicyKind.Role, IspRoles.Tenant));
        request.RecipientAccess.Should().Be(OrderRecipientAccess.Inform);
    }

    [Fact]
    public void IspRoles_AreTheNamesTheSeederCreates()
    {
        IspRoles.Tenant.Should().Be("IspTenant");
        IspRoles.Supervisor.Should().Be("Supervisor");
        IspRoles.Administrator.Should().Be("Administrator");
    }

    // ── What the rule chain does with them ────────────────────────────────────

    [Fact]
    public void Dispute_IspTenantMember_IsAllowed() =>
        Rules().Evaluate(Member(IspRoles.Tenant), OrderActionType.Dispute, Notice()).Allowed.Should().BeTrue();

    [Fact]
    public void Dispute_MemberWithOnlySupervisor_IsDeniedByTheActionPermissionRule()
    {
        var verdict = Rules().Evaluate(Member(IspRoles.Supervisor), OrderActionType.Dispute, Notice());

        verdict.Allowed.Should().BeFalse();
        verdict.Reason.Should().Be("Denied by rule 'OrderTypeActionPermissionRule'.");
    }

    [Theory]
    [InlineData(IspRoles.Supervisor, OrderActionType.Approve)]
    [InlineData(IspRoles.Supervisor, OrderActionType.Reject)]
    [InlineData(IspRoles.Supervisor, OrderActionType.FlagClandestine)]
    [InlineData(IspRoles.Administrator, OrderActionType.Approve)]
    [InlineData(IspRoles.Administrator, OrderActionType.Reject)]
    [InlineData(IspRoles.Administrator, OrderActionType.FlagClandestine)]
    public void StaffActions_Supervisor_AndAdministrator_AreAllowed(string role, OrderActionType action) =>
        Rules().Evaluate(Member(role), action, Notice()).Allowed.Should().BeTrue();

    [Theory]
    [InlineData(OrderActionType.Approve)]
    [InlineData(OrderActionType.Reject)]
    [InlineData(OrderActionType.FlagClandestine)]
    public void StaffActions_IspTenantMember_AreDenied(OrderActionType action) =>
        Rules().Evaluate(Member(IspRoles.Tenant), action, Notice()).Allowed.Should().BeFalse();

    [Fact]
    public void CanCreate_AttachmentRequest_IspTenantMayAndSupervisorMayNot()
    {
        var rules = Rules();
        var request = TenantAttachmentRequestOrderTypes.CreateAttachmentRequest();

        rules.CanCreate(Member(IspRoles.Tenant), request).Should().BeTrue();
        rules.CanCreate(Member(IspRoles.Supervisor), request).Should().BeFalse();
    }

    [Fact]
    public void CanCreate_ClandestineNotice_SupervisorMayAndIspTenantMayNot()
    {
        var rules = Rules();
        var notice = ClandestineAttachmentOrderTypes.CreateClandestineNotice();

        rules.CanCreate(Member(IspRoles.Supervisor), notice).Should().BeTrue();
        rules.CanCreate(Member(IspRoles.Tenant), notice).Should().BeFalse();
    }

    [Fact]
    public void Dispute_RecipientOrganizationWithIspTenant_IsAllowed_AlongItsTransitionOnly()
    {
        var recipient = new Guid("b0b0b0b0-0000-4000-8000-000000000364");
        var order = Notice();
        order.Dispatches.Add(new OrderDispatch(recipient.ToString(), DispatchTargetType.Organization, "admin", Now.UtcDateTime));
        var principal = new WorkflowPrincipal("user-2", recipient.ToString(), [IspRoles.Tenant], [], []);
        var rules = Rules();

        var along = rules.EvaluateAppend(
            principal, order, new OrderActionLog(OrderActionType.Dispute, "user-2", Now.UtcDateTime, "evidence", ClandestineAttachmentWorkflowGraph.Disputed));
        var off = rules.EvaluateAppend(
            principal, order, new OrderActionLog(OrderActionType.Dispute, "user-2", Now.UtcDateTime, "evidence", ClandestineAttachmentWorkflowGraph.Regularized));

        along.Allowed.Should().BeTrue();
        off.Allowed.Should().BeFalse();
        off.Code.Should().Be(RuleDenialCode.DeniedByRecipientAccess);
    }

    // ── Regression: the shared grant matcher keeps matching case-insensitively ─

    [Fact]
    public void CrossOrgGrantMatcher_ResourceTypeDifferingInCase_StillMatches()
    {
        var grantee = new Guid("c0c0c0c0-0000-4000-8000-000000000364");
        var principal = new WorkflowPrincipal("user-3", grantee.ToString(), [], [], [])
        {
            OrgGrants = [new WorkflowOrgGrant(Owner.ToString(), "serviceorder", ["SERVICEORDERS:VIEW"], null)],
        };
        var order = Notice();

        CrossOrgGrantMatcher.CoversAction(principal, OrderActionType.View, order, Now.UtcDateTime).Should().BeTrue();
        CrossOrgGrantMatcher.CoversOrder(principal, order, Now.UtcDateTime).Should().BeTrue();
        Rules().PassesOrganizationGate(principal, order).Should().BeTrue();
    }
}
