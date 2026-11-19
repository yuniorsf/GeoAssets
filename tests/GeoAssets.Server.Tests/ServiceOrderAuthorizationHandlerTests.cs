using System.Security.Claims;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// The service-order requirements (XD01-364) in isolation: <see cref="ServiceOrderActionAuthorizationHandler"/> and
/// <see cref="ServiceOrderCreateAuthorizationHandler"/> answer with the real <see cref="ServiceOrderRules"/> over the
/// real ISP order types, for a caller whose organization and roles the fake identity store names. Over HTTP the same
/// handlers are covered by <see cref="IspTenantPortalAuthorizationTests"/>.
/// </summary>
public sealed class ServiceOrderAuthorizationHandlerTests
{
    private static readonly Guid OrgA = new("a0a0a0a0-0000-4000-8000-000000000364");
    private static readonly Guid OrgB = new("b0b0b0b0-0000-4000-8000-000000000364");
    private static readonly DateTime At = new(2026, 11, 17, 9, 0, 0, DateTimeKind.Utc);

    private sealed class FakeAuthorizationService(Guid organizationId, IReadOnlyList<string> roles) : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) =>
            Task.FromResult(new AuthorizationContext
            {
                User = new AppUser
                {
                    Id = Guid.NewGuid(),
                    Email = "user@example.com",
                    DisplayName = "User",
                    CreatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                    OrganizationId = organizationId,
                },
                Roles = roles,
                Claims = [],
                Permissions = [],
            });
    }

    private sealed class NoGrantsRepository : IOrganizationGrantRepository
    {
        public Task<OrganizationGrant?> GetByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetAllAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsAsync(Guid granteeOrganizationId, Guid resourceOrganizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>([]);
        public Task<IReadOnlyList<OrganizationGrant>> GetActiveGrantsForGranteeAsync(Guid granteeOrganizationId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<OrganizationGrant>>([]);
        public Task AddAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UpdateAsync(OrganizationGrant grant, CancellationToken ct = default) => throw new NotSupportedException();
        public Task SaveChangesAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<OrganizationGrantSyncEntry>> GetSyncBatchAsync(long afterSequence, int take, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static OrderTypeRegistry Registry()
    {
        var registry = new OrderTypeRegistry();
        ClandestineAttachmentOrderTypes.RegisterAll(registry);
        TenantAttachmentRequestOrderTypes.RegisterAll(registry);
        return registry;
    }

    private static ServerWorkflowPrincipalFactory Principals(Guid organizationId, params string[] roles) =>
        new(new FakeAuthorizationService(organizationId, roles), new NoGrantsRepository());

    private static ServiceOrderActionAuthorizationHandler ActionHandler(Guid organizationId, params string[] roles) =>
        new(new ServiceOrderRules(Registry()), Principals(organizationId, roles));

    private static ServiceOrderCreateAuthorizationHandler CreateHandler(OrderTypeRegistry registry, params string[] roles) =>
        new(new ServiceOrderRules(registry), registry, Principals(OrgA, roles));

    private static ServiceOrder Notice(string status = ClandestineAttachmentWorkflowGraph.CaboARevelia, bool dispatchedToOrgB = false)
    {
        var order = new ServiceOrder
        {
            Id = "notice-1",
            OrderTypeId = ClandestineAttachmentOrderTypes.ClandestineNoticeId,
            Status = status,
            OrganizationId = OrgA,
            CreatedBy = "someone-else",
        };
        if (dispatchedToOrgB)
            order.Dispatches.Add(new OrderDispatch(OrgB.ToString(), DispatchTargetType.Organization, "admin", At));
        return order;
    }

    private static AuthorizationHandlerContext Context(IAuthorizationRequirement requirement, object? resource = null) =>
        new([requirement], new ClaimsPrincipal(new ClaimsIdentity(authenticationType: "TestAuth")), resource);

    private static ServiceOrderAuthorizationFailureReason SingleReason(AuthorizationHandlerContext context) =>
        context.FailureReasons.Should().ContainSingle().Which.Should().BeOfType<ServiceOrderAuthorizationFailureReason>().Subject;

    // ── Action requirement ────────────────────────────────────────────────────

    [Fact]
    public async Task Action_AllowedByTheRuleChain_Succeeds()
    {
        var context = Context(new ServiceOrderActionRequirement(OrderActionType.Dispute), Notice());

        await ActionHandler(OrgA, IspRoles.Tenant).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
        context.HasFailed.Should().BeFalse();
    }

    [Fact]
    public async Task Action_DeniedByTheRuleChain_FailsWithAReasonCarryingTheVerdict()
    {
        var requirement = new ServiceOrderActionRequirement(OrderActionType.Dispute);
        var context = Context(requirement, Notice());

        await ActionHandler(OrgA, IspRoles.Supervisor).HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        context.HasFailed.Should().BeTrue();
        var reason = SingleReason(context);
        reason.Verdict.Should().NotBeNull();
        reason.Verdict!.Allowed.Should().BeFalse();
        reason.Verdict.Action.Should().Be(OrderActionType.Dispute);
        reason.Verdict.Code.Should().Be(RuleDenialCode.DeniedByRule);
        reason.Message.Should().Be(reason.Verdict.Reason);
    }

    [Fact]
    public async Task Action_CallerOutsideTheOrdersOrganization_FailsWithAnOutsideOrganizationVerdict()
    {
        var context = Context(new ServiceOrderActionRequirement(OrderActionType.Dispute), Notice());

        await ActionHandler(OrgB, IspRoles.Tenant).HandleAsync(context);

        context.HasFailed.Should().BeTrue();
        SingleReason(context).Verdict!.IsOutsideOrganization.Should().BeTrue();
    }

    [Fact]
    public async Task Action_FromAnEntry_IsEvaluatedAsAnAppend_SoARecipientMovesOnlyAlongItsTransition()
    {
        var order = Notice(dispatchedToOrgB: true);
        var bare = Context(new ServiceOrderActionRequirement(OrderActionType.Dispute), order);
        var along = Context(
            new ServiceOrderActionRequirement(new OrderActionLog(OrderActionType.Dispute, "u", At, "evidence", ClandestineAttachmentWorkflowGraph.Disputed)),
            order);
        var off = Context(
            new ServiceOrderActionRequirement(new OrderActionLog(OrderActionType.Dispute, "u", At, "evidence", ClandestineAttachmentWorkflowGraph.Regularized)),
            order);
        var recipient = ActionHandler(OrgB, IspRoles.Tenant);

        await recipient.HandleAsync(bare);
        await recipient.HandleAsync(along);
        await recipient.HandleAsync(off);

        bare.HasSucceeded.Should().BeTrue("Evaluate alone does not look at the entry's resulting status");
        along.HasSucceeded.Should().BeTrue();
        off.HasSucceeded.Should().BeFalse();
        SingleReason(off).Verdict!.Code.Should().Be(RuleDenialCode.DeniedByRecipientAccess);
    }

    [Fact]
    public void ActionRequirement_NullEntry_Throws()
    {
        var act = () => new ServiceOrderActionRequirement((OrderActionLog)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ActionRequirement_FromABareAction_HasNoEntry() =>
        new ServiceOrderActionRequirement(OrderActionType.Approve).Entry.Should().BeNull();

    // ── Create requirement ────────────────────────────────────────────────────

    [Fact]
    public async Task Create_CallerAdmittedByACreationPolicy_Succeeds()
    {
        var context = Context(new ServiceOrderCreateRequirement(TenantAttachmentRequestOrderTypes.AttachmentRequestId));

        await CreateHandler(Registry(), IspRoles.Tenant).HandleAsync(context);

        context.HasSucceeded.Should().BeTrue();
    }

    [Fact]
    public async Task Create_CallerNoCreationPolicyAdmits_FailsWithAMessageAndNoVerdict()
    {
        var context = Context(new ServiceOrderCreateRequirement(TenantAttachmentRequestOrderTypes.AttachmentRequestId));

        await CreateHandler(Registry(), IspRoles.Supervisor).HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        var reason = SingleReason(context);
        reason.Verdict.Should().BeNull();
        reason.Message.Should().Be($"No creation policy of order type '{TenantAttachmentRequestOrderTypes.AttachmentRequestId}' admits the caller.");
    }

    [Fact]
    public async Task Create_UnregisteredOrderType_FailsWithAMessageAndNoVerdict()
    {
        var context = Context(new ServiceOrderCreateRequirement("not-registered"));

        await CreateHandler(Registry(), IspRoles.Tenant, IspRoles.Administrator).HandleAsync(context);

        context.HasSucceeded.Should().BeFalse();
        var reason = SingleReason(context);
        reason.Verdict.Should().BeNull();
        reason.Message.Should().Be("Order type 'not-registered' is not registered.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void CreateRequirement_BlankOrderTypeId_Throws(string? orderTypeId)
    {
        var act = () => new ServiceOrderCreateRequirement(orderTypeId!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void FailureReason_NullVerdict_Throws()
    {
        var act = () => new ServiceOrderAuthorizationFailureReason(ActionHandler(OrgA), (RuleEvaluationResult)null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
