using FluentAssertions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rules;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Tests.Rules;

/// <summary>
/// XD01-326 — cross-organization grants that require a scoped role (<see cref="WorkflowOrgGrant.RequiresScopedRole"/>) and
/// grants narrowed to order types (<see cref="WorkflowOrgGrant.OrderTypeIds"/>), through <see cref="ServiceOrderRules"/>.
///
/// The scenario is the ticket's: COELBA grants BIMBO <c>serviceorders:view</c> and <c>serviceorders:execute</c> on its
/// "cut" orders in one município, usable only through roles held on COELBA. A BIMBO user with a role on COELBA granting
/// those permissions may view and execute such an order, and nothing else; its home roles never count.
/// </summary>
public class ServiceOrderScopedRoleGrantTests
{
    private const string UserId = "bimbo-tech-1";
    private const string Scope = "municipio-123";
    private const string View = "serviceorders:view";
    private const string Execute = "serviceorders:execute";

    private static readonly Guid Coelba = new("c0e1ba00-0000-4000-8000-000000000326");
    private static readonly Guid Bimbo = new("b1b00000-0000-4000-8000-000000000326");
    private static readonly Guid Third = new("7e1d0000-0000-4000-8000-000000000326");
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    private static ServiceOrderRules Rules(params OrderType[] extraTypes)
    {
        var registry = new OrderTypeRegistry();
        registry.Register(new OrderType { Id = "cut", DisplayName = "Cut" });
        registry.Register(new OrderType { Id = "reconnect", DisplayName = "Reconnect" });
        foreach (var type in extraTypes) registry.Register(type);
        return new ServiceOrderRules(registry, timeProvider: new FakeTimeProvider(Now));
    }

    private static WorkflowOrgGrant ScopedGrant(
        Guid? resource = null, string? scopeId = Scope, string? requiredRole = null, DateTime? expiresAt = null,
        string[]? orderTypeIds = null, string[]? actions = null) =>
        new((resource ?? Coelba).ToString(), "ServiceOrder", actions ?? [View, Execute], requiredRole, scopeId)
        {
            RequiresScopedRole = true,
            OrderTypeIds = orderTypeIds ?? ["cut"],
            ExpiresAt = expiresAt,
        };

    /// <summary>A BIMBO user: <paramref name="homeRoles"/>/<paramref name="homePermissions"/> at home, and what it holds on COELBA.</summary>
    private static WorkflowPrincipal Principal(
        WorkflowOrgGrant[] grants,
        string[]? onCoelba = null,
        string[]? rolesOnCoelba = null,
        string[]? homeRoles = null,
        string[]? homePermissions = null) =>
        new(UserId, Bimbo.ToString(), homeRoles ?? [], [], homePermissions ?? [])
        {
            OrgGrants = grants,
            ScopedRoleNames = new Dictionary<Guid, IReadOnlyList<string>> { [Coelba] = rolesOnCoelba ?? ["Contractor C1"] },
            ScopedPermissionCodes = new Dictionary<Guid, IReadOnlyList<string>> { [Coelba] = onCoelba ?? [View, Execute] },
        };

    private static ServiceOrder Order(string orderTypeId = "cut", Guid? owner = null, string? scopeId = Scope, string? assignedTo = null) => new()
    {
        Id = "ord-326",
        OrderTypeId = orderTypeId,
        Status = ServiceOrderStatus.Pending,
        OrganizationId = owner ?? Coelba,
        CreatedBy = "coelba-dispatcher",
        AssignedTo = assignedTo,
        Attributes = scopeId is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal) { ["scopeId"] = scopeId },
    };

    // ── The ticket's acceptance matrix ─────────────────────────────────────────

    [Fact]
    public void A_user_holding_the_scoped_permission_executes_an_entitled_order()
    {
        var principal = Principal([ScopedGrant()]);

        var result = Rules().Evaluate(principal, OrderActionType.Execute, Order());

        result.Allowed.Should().BeTrue();
        Rules().CanView(principal, Order()).Should().BeTrue();
    }

    [Fact]
    public void An_order_of_another_type_is_outside_the_grant()
    {
        var result = Rules().Evaluate(Principal([ScopedGrant()]), OrderActionType.Execute, Order(orderTypeId: "reconnect"));

        result.Allowed.Should().BeFalse();
        result.Code.Should().Be(RuleDenialCode.OutsideOrganization);
    }

    [Fact]
    public void Order_types_compare_ignoring_case()
    {
        Rules().Evaluate(Principal([ScopedGrant()]), OrderActionType.Execute, Order(orderTypeId: "CUT"))
            .Allowed.Should().BeTrue();
    }

    [Theory]
    [InlineData("municipio-999")]
    [InlineData(null)]
    public void An_order_outside_the_grant_scope_is_refused(string? orderScope)
    {
        var result = Rules().Evaluate(Principal([ScopedGrant()]), OrderActionType.Execute, Order(scopeId: orderScope));

        result.Allowed.Should().BeFalse();
        result.Code.Should().Be(RuleDenialCode.OutsideOrganization);
    }

    [Fact]
    public void The_permission_held_only_at_home_opens_nothing()
    {
        var principal = Principal(
            [ScopedGrant()], onCoelba: [], rolesOnCoelba: [], homeRoles: ["Administrator"], homePermissions: [View, Execute]);

        var result = Rules().Evaluate(principal, OrderActionType.Execute, Order());

        result.Allowed.Should().BeFalse();
        result.Code.Should().Be(RuleDenialCode.OutsideOrganization);
        Rules().PassesOrganizationGate(principal, Order()).Should().BeFalse();
    }

    [Fact]
    public void Without_an_active_grant_the_scoped_role_opens_nothing()
    {
        // A revoked grant is one no host resolves onto the principal; an expired one may still be on a long-lived one.
        var revoked = Principal([]);
        var expired = Principal([ScopedGrant(expiresAt: Now.UtcDateTime.AddMinutes(-1))]);

        Rules().Evaluate(revoked, OrderActionType.Execute, Order()).Code.Should().Be(RuleDenialCode.OutsideOrganization);
        Rules().Evaluate(expired, OrderActionType.Execute, Order()).Code.Should().Be(RuleDenialCode.OutsideOrganization);
    }

    [Fact]
    public void A_role_scoped_to_one_organization_grants_nothing_on_a_third_one()
    {
        // The third organization grants BIMBO the same scoped access; the user's roles are on COELBA only.
        var principal = Principal([ScopedGrant(), ScopedGrant(resource: Third)]);

        var result = Rules().Evaluate(principal, OrderActionType.Execute, Order(owner: Third));

        result.Allowed.Should().BeFalse();
        result.Code.Should().Be(RuleDenialCode.OutsideOrganization);
    }

    // ── The grant is the ceiling, the roles held on the owner decide within it ─

    [Fact]
    public void An_action_the_grant_does_not_list_is_refused_whatever_the_roles_held()
    {
        var principal = Principal(
            [ScopedGrant()], onCoelba: [View, Execute, "serviceorders:complete"], rolesOnCoelba: ["Administrator"],
            homeRoles: ["Administrator"]);

        var result = Rules().Evaluate(principal, OrderActionType.Complete, Order());

        result.Allowed.Should().BeFalse();
        result.Code.Should().Be(RuleDenialCode.NoRuleGranted);
        result.Reason.Should().Be(ServiceOrderRules.NotGrantedOnOrganizationReason);
    }

    [Fact]
    public void A_listed_action_not_held_on_the_owner_is_refused_even_for_a_home_supervisor()
    {
        // Viewing is held on COELBA, so the order is reachable; executing is held only at home.
        var principal = Principal(
            [ScopedGrant()], onCoelba: [View], homeRoles: ["Supervisor", "Administrator"], homePermissions: [Execute]);

        Rules().CanView(principal, Order()).Should().BeTrue();
        var result = Rules().Evaluate(principal, OrderActionType.Execute, Order());

        result.Allowed.Should().BeFalse();
        result.Reason.Should().Be(ServiceOrderRules.NotGrantedOnOrganizationReason);
    }

    [Fact]
    public void An_assignee_reaching_the_order_through_a_scoped_grant_is_still_held_to_it()
    {
        // The assignee rule alone would allow completing; the grant does not list it.
        var principal = Principal([ScopedGrant()]);

        Rules().Evaluate(principal, OrderActionType.Complete, Order(assignedTo: UserId)).Allowed.Should().BeFalse();
        Rules().Evaluate(principal, OrderActionType.Execute, Order(assignedTo: UserId)).Allowed.Should().BeTrue();
    }

    [Fact]
    public void The_order_types_own_requirements_are_checked_against_the_roles_held_on_the_owner()
    {
        var gated = new OrderType
        {
            Id = "gated-cut",
            DisplayName = "Gated cut",
            ActionPermissions = [new(OrderActionType.Execute, PolicyKind.Role, "Lineman")],
        };
        var grant = ScopedGrant(orderTypeIds: ["gated-cut"]);

        var homeLineman = Principal([grant], homeRoles: ["Lineman"]);
        var linemanOnCoelba = Principal([grant], rolesOnCoelba: ["Lineman"]);

        Rules(gated).Evaluate(homeLineman, OrderActionType.Execute, Order(orderTypeId: "gated-cut"))
            .Code.Should().Be(RuleDenialCode.DeniedByRule);
        Rules(gated).Evaluate(linemanOnCoelba, OrderActionType.Execute, Order(orderTypeId: "gated-cut"))
            .Allowed.Should().BeTrue();
    }

    [Fact]
    public void A_required_role_is_matched_against_the_roles_held_on_the_owner()
    {
        var grant = ScopedGrant(requiredRole: "Contractor C1");

        var holdsItOnCoelba = Principal([grant]);
        var holdsItAtHome = Principal([grant], rolesOnCoelba: ["Someone else"], homeRoles: ["Contractor C1"]);

        Rules().Evaluate(holdsItOnCoelba, OrderActionType.Execute, Order()).Allowed.Should().BeTrue();
        Rules().Evaluate(holdsItAtHome, OrderActionType.Execute, Order()).Code.Should().Be(RuleDenialCode.OutsideOrganization);
    }

    [Fact]
    public void A_target_write_is_held_to_the_scoped_grant_too()
    {
        var annotates = Principal([ScopedGrant(actions: [View, "serviceorders:annotate"])], onCoelba: [View, "serviceorders:annotate"]);
        var viewsOnly = Principal([ScopedGrant(actions: [View, "serviceorders:annotate"])], onCoelba: [View], homeRoles: ["Administrator"]);

        Rules().EvaluateTargetWrite(annotates, Order(), OrderTargetWrite.RecordWork).Allowed.Should().BeTrue();
        Rules().EvaluateTargetWrite(viewsOnly, Order(), OrderTargetWrite.RecordWork).Allowed.Should().BeFalse();
    }

    // ── Organization-wide grants keep today's semantics ────────────────────────

    [Fact]
    public void An_organization_wide_grant_beside_a_scoped_one_keeps_the_home_roles_deciding()
    {
        // Organization-wide, the grant opens the order to BIMBO and the home Supervisor role approves, as before XD01-326.
        var orgWide = ScopedGrant(actions: [View]) with { RequiresScopedRole = false };
        var principal = Principal([orgWide, ScopedGrant()], onCoelba: [], homeRoles: ["Supervisor"]);

        Rules().Evaluate(principal, OrderActionType.Approve, Order()).Allowed.Should().BeTrue();
    }

    [Fact]
    public void An_organization_wide_grant_narrowed_to_order_types_opens_only_those()
    {
        var grant = new WorkflowOrgGrant(Coelba.ToString(), "ServiceOrder", [View], RequiredRole: null) { OrderTypeIds = ["cut"] };
        var principal = new WorkflowPrincipal(UserId, Bimbo.ToString(), ["Supervisor"], [], []) { OrgGrants = [grant] };

        Rules().CanView(principal, Order(scopeId: null)).Should().BeTrue();
        Rules().PassesOrganizationGate(principal, Order(orderTypeId: "reconnect")).Should().BeFalse();
    }

    // ── WorkflowPrincipal's view of an organization ────────────────────────────

    [Fact]
    public void A_principal_holds_its_home_set_at_home_and_only_scoped_sets_elsewhere()
    {
        var principal = Principal([], homeRoles: ["Supervisor"], homePermissions: ["serviceorders:approve"]);

        principal.RoleNamesOn(Bimbo).Should().Equal("Supervisor");
        principal.PermissionCodesOn(Bimbo).Should().Equal("serviceorders:approve");
        principal.RoleNamesOn(Coelba).Should().Equal("Contractor C1");
        principal.PermissionCodesOn(Coelba).Should().Equal(View, Execute);
        principal.RoleNamesOn(Third).Should().BeEmpty();
        principal.PermissionCodesOn(Third).Should().BeEmpty();

        principal.HasRoleOn(Coelba, "contractor c1").Should().BeTrue();
        principal.HasPermissionOn(Coelba, "SERVICEORDERS:EXECUTE").Should().BeTrue();
        principal.HasPermissionOn(Coelba, "serviceorders:approve").Should().BeFalse();
        principal.HasPermission(Execute).Should().BeFalse("a scoped permission never reaches the home set");
    }

    [Fact]
    public void A_principal_with_no_organization_has_only_scoped_sets()
    {
        var principal = new WorkflowPrincipal(UserId, null, ["Supervisor"], [], [View])
        {
            ScopedPermissionCodes = new Dictionary<Guid, IReadOnlyList<string>> { [Coelba] = [Execute] },
        };

        principal.PermissionCodesOn(Coelba).Should().Equal(Execute);
        principal.RoleNamesOn(Coelba).Should().BeEmpty();
    }

    [Fact]
    public void On_an_organization_the_principal_keeps_its_identity_and_trades_its_roles()
    {
        var principal = Principal([ScopedGrant()], homeRoles: ["Supervisor"], homePermissions: ["serviceorders:approve"]);

        var onCoelba = principal.OnOrganization(Coelba);

        onCoelba.UserId.Should().Be(UserId);
        onCoelba.OrganizationId.Should().Be(Bimbo.ToString());
        onCoelba.OrgGrants.Should().BeSameAs(principal.OrgGrants);
        onCoelba.RoleNames.Should().Equal("Contractor C1");
        onCoelba.PermissionCodes.Should().Equal(View, Execute);
    }

    [Fact]
    public void Grants_and_principals_default_to_the_organization_wide_semantics()
    {
        var grant = new WorkflowOrgGrant(Coelba.ToString(), null, [View], RequiredRole: null);

        grant.RequiresScopedRole.Should().BeFalse();
        grant.OrderTypeIds.Should().BeEmpty();
        WorkflowPrincipal.Anonymous.ScopedRoleNames.Should().BeEmpty();
        WorkflowPrincipal.Anonymous.ScopedPermissionCodes.Should().BeEmpty();
    }
}
