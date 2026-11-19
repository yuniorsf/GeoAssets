using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Server.Modules.NeutralOperator;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Who may do what through the ISP tenant portal and the generic action route (XD01-364, ADR-001 smell H): both decide with
/// <c>ServiceOrderRules</c> — the order type's creation and action policies, the organization gate and the dispatch
/// recipient's actions — instead of a fixed permission code. Inverted by XD01-364 from the XD01-355 characterization facts
/// that pinned the permission-based behaviour.
/// </summary>
public sealed class IspTenantPortalAuthorizationTests
{
    private const string NoticeId = "notice-1";
    private const string Evidence = "receipt-digest-abc123";
    private const string DenialByActionPermissions = "Denied by rule 'OrderTypeActionPermissionRule'.";

    private static Task<OrderTargetsApiHost> StartAsync() => OrderTargetsApiHost.StartAsync(mapOrderRoutes: true, mapIspPortalRoutes: true);

    private static HttpClient PortalClientFor(OrderTargetsApiHost host, string token)
    {
        var client = host.Server.CreateClient();
        client.DefaultRequestHeaders.Add(OrderTargetsApiHost.TokenHeader, token);
        return client;
    }

    private static Uri Dispute(string id = NoticeId) => new($"/api/isp/tenant-portal/flagged-notices/{id}/dispute", UriKind.Relative);

    private static object Action(OrderActionType action, string? comment = null, string? resultingStatus = null) => new
    {
        action = (int)action,
        performedBy = "whoever-the-body-says",
        performedAt = OrderTargetsApiHost.Now.UtcDateTime,
        comment,
        resultingStatus,
    };

    /// <summary>A notice of organization A, in <paramref name="status"/> (flagged through the real service when it is CaboARevelia).</summary>
    private static async Task SeedNoticeAsync(OrderTargetsApiHost host, bool flagged = true)
    {
        await host.SeedOrderAsync(
            NoticeId, ClandestineAttachmentOrderTypes.ClandestineNoticeId, ClandestineAttachmentWorkflowGraph.Active, OrderTargetsApiHost.OrgA);
        if (flagged) await new ClandestineAttachmentService(host.Orders, host.Clock).FlagAsync(NoticeId);
    }

    private static Task DispatchToOrgBAsync(OrderTargetsApiHost host, string id = NoticeId) =>
        host.Orders.AppendDispatchAsync(
            id,
            new OrderDispatch(
                OrderTargetsApiHost.OrgB.ToString(), DispatchTargetType.Organization, "admin", OrderTargetsApiHost.Now.UtcDateTime, null));

    private static async Task<string> StatusOfAsync(OrderTargetsApiHost host, string id = NoticeId) =>
        (await host.Orders.GetByIdAsync(id))!.Status;

    // ── Inverted characterization facts ───────────────────────────────────────

    /// <summary>
    /// Inverted by XD01-364 (was <c>Characterization_UserHoldingOnlyServiceOrdersCreate_CanDisputeFlaggedNotice</c>):
    /// disputing a flagged notice is an action authorized by the notice type's action permissions, not by a create permission.
    /// </summary>
    [Fact]
    public async Task Dispute_UserHoldingOnlyServiceOrdersCreate_Returns403WithRuleReason()
    {
        await using var host = await StartAsync();
        await SeedNoticeAsync(host);
        using var client = PortalClientFor(host, "create-only-a");

        var response = await client.PostAsJsonAsync(Dispute(), new SubmitDisputeRequest(Evidence));

        var body = await response.ReadJsonAsync(HttpStatusCode.Forbidden);
        body.GetProperty("reason").GetString().Should().Be(DenialByActionPermissions);
        (await StatusOfAsync(host)).Should().Be(ClandestineAttachmentWorkflowGraph.CaboARevelia);
    }

    /// <summary>
    /// Inverted by XD01-364 (was <c>Characterization_OtherOrganizationsAttachmentRequest_Returns403InsteadOf404</c>): the
    /// portal answers 404, as for an unknown id, so a refusal never confirms that the id exists.
    /// </summary>
    [Fact]
    public async Task GetAttachmentRequest_OtherOrganization_Returns404()
    {
        await using var host = await StartAsync();
        await host.SeedOrderAsync(
            "request-of-a", TenantAttachmentRequestOrderTypes.AttachmentRequestId,
            TenantAttachmentRequestWorkflowGraph.Submitted, OrderTargetsApiHost.OrgA);
        using var client = PortalClientFor(host, "tenant-b");

        var response = await client.GetAsync(Path("/api/isp/tenant-portal/attachment-requests/request-of-a"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Inverted by XD01-364 (was <c>Characterization_OrderDispatchedToOtherOrganization_IsReadableViaWorkflowButRefusedByPortal</c>):
    /// the workflow API and the portal agree on who may read the order, because both pass the organization gate, which admits
    /// the recipients of a dispatch.
    /// </summary>
    [Fact]
    public async Task OrderDispatchedToOtherOrganization_IsReadableViaWorkflowAndPortal()
    {
        await using var host = await StartAsync();
        await host.SeedOrderAsync(
            "request-of-a", TenantAttachmentRequestOrderTypes.AttachmentRequestId,
            TenantAttachmentRequestWorkflowGraph.Submitted, OrderTargetsApiHost.OrgA);
        await DispatchToOrgBAsync(host, "request-of-a");
        using var workflowClient = host.ClientFor("tenant-b");
        using var portalClient = PortalClientFor(host, "tenant-b");

        var viaWorkflow = await workflowClient.GetAsync(Path("service-orders/request-of-a"));
        var viaPortal = await portalClient.GetAsync(Path("/api/isp/tenant-portal/attachment-requests/request-of-a"));

        viaWorkflow.StatusCode.Should().Be(HttpStatusCode.OK);
        viaPortal.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Dispute through the portal ────────────────────────────────────────────

    [Fact]
    public async Task Dispute_IspTenantOfTheOrdersOrganization_TransitionsToDisputed()
    {
        await using var host = await StartAsync();
        await SeedNoticeAsync(host);
        using var client = PortalClientFor(host, "tenant-a");

        var response = await client.PostAsJsonAsync(Dispute(), new SubmitDisputeRequest(Evidence));

        var body = await response.ReadJsonAsync();
        body.GetProperty("status").GetString().Should().Be(ClandestineAttachmentWorkflowGraph.Disputed);
        (await StatusOfAsync(host)).Should().Be(ClandestineAttachmentWorkflowGraph.Disputed);
    }

    [Fact]
    public async Task Dispute_OtherOrganizationsNotice_Returns404_AndDoesNotTransition()
    {
        await using var host = await StartAsync();
        await SeedNoticeAsync(host);
        using var client = PortalClientFor(host, "tenant-b");

        var response = await client.PostAsJsonAsync(Dispute(), new SubmitDisputeRequest(Evidence));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await StatusOfAsync(host)).Should().Be(ClandestineAttachmentWorkflowGraph.CaboARevelia);
    }

    [Fact]
    public async Task Dispute_DispatchedRecipientOrganizationWithIspTenant_CanDispute()
    {
        await using var host = await StartAsync();
        await SeedNoticeAsync(host);
        await DispatchToOrgBAsync(host);
        using var client = PortalClientFor(host, "tenant-b");

        var response = await client.PostAsJsonAsync(Dispute(), new SubmitDisputeRequest(Evidence));

        var body = await response.ReadJsonAsync();
        body.GetProperty("status").GetString().Should().Be(ClandestineAttachmentWorkflowGraph.Disputed);
        (await StatusOfAsync(host)).Should().Be(ClandestineAttachmentWorkflowGraph.Disputed);
    }

    // ── The generic action route ──────────────────────────────────────────────

    [Theory]
    [InlineData(OrderActionType.FlagClandestine, ClandestineAttachmentWorkflowGraph.Active, ClandestineAttachmentWorkflowGraph.CaboARevelia)]
    [InlineData(OrderActionType.Approve, ClandestineAttachmentWorkflowGraph.CaboARevelia, ClandestineAttachmentWorkflowGraph.Regularized)]
    public async Task ActionsRoute_DispatchedRecipient_CannotFlagOutsideRecipientActions(
        OrderActionType action, string status, string resultingStatus)
    {
        await using var host = await StartAsync();
        await SeedNoticeAsync(host, flagged: !string.Equals(status, ClandestineAttachmentWorkflowGraph.Active, StringComparison.Ordinal));
        await DispatchToOrgBAsync(host);
        using var client = host.ClientFor("supervisor-b");

        var response = await client.PostJsonAsync($"service-orders/{NoticeId}/actions", Action(action, "go", resultingStatus));

        response.IsSuccessStatusCode.Should().BeFalse("a recipient acts only within the type's RecipientActions, and Dispute is the only one");
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Forbidden, HttpStatusCode.NotFound);
        (await StatusOfAsync(host)).Should().Be(status);
    }

    [Fact]
    public async Task ActionsRoute_Dispute_GoesThroughTheClandestineHandler()
    {
        await using var host = await StartAsync();
        await SeedNoticeAsync(host);
        using var client = host.ClientFor("tenant-a");

        var response = await client.PostJsonAsync(
            $"service-orders/{NoticeId}/actions",
            Action(OrderActionType.Dispute, "evidence", ClandestineAttachmentWorkflowGraph.Disputed));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var notice = (await host.Orders.GetByIdAsync(NoticeId))!;
        notice.Status.Should().Be(ClandestineAttachmentWorkflowGraph.Disputed);
        notice.Attributes.Should().ContainKey(ClandestineAttachmentService.AttrDisputeSubmittedAtUtc,
            "the handler ran the service, which stamps the dispute's deadlines — the default append would not");
        notice.Attributes[ClandestineAttachmentService.AttrDisputeEvidenceReference].Should().Be("evidence");
    }

    [Fact]
    public async Task ActionsRoute_Dispute_WithoutEvidence_Returns400Problem()
    {
        await using var host = await StartAsync();
        await SeedNoticeAsync(host);
        using var client = host.ClientFor("tenant-a");

        var response = await client.PostJsonAsync(
            $"service-orders/{NoticeId}/actions",
            Action(OrderActionType.Dispute, comment: null, ClandestineAttachmentWorkflowGraph.Disputed));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "clandestine.evidence-required");
        (await StatusOfAsync(host)).Should().Be(ClandestineAttachmentWorkflowGraph.CaboARevelia);
    }

    [Fact]
    public async Task ActionsRoute_Approve_AfterWindowElapsed_Returns409Problem()
    {
        await using var host = await StartAsync();
        await SeedNoticeAsync(host);
        host.Clock.Advance(TimeSpan.FromDays(31));
        using var client = host.ClientFor("supervisor-a");

        var response = await client.PostJsonAsync(
            $"service-orders/{NoticeId}/actions",
            Action(OrderActionType.Approve, resultingStatus: ClandestineAttachmentWorkflowGraph.Regularized));

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "clandestine.window-elapsed");
        (await StatusOfAsync(host)).Should().Be(ClandestineAttachmentWorkflowGraph.CaboARevelia);
    }

    // ── Submitting an attachment request ──────────────────────────────────────

    [Fact]
    public async Task SubmitAttachmentRequest_WithoutIspTenantRole_Returns403()
    {
        await using var host = await StartAsync();
        using var client = PortalClientFor(host, "create-only-a");

        var response = await client.PostAsJsonAsync(
            Path("/api/isp/tenant-portal/attachment-requests"), new SubmitAttachmentRequestRequest("pole-123", "New fiber attachment"));

        var body = await response.ReadJsonAsync(HttpStatusCode.Forbidden);
        body.GetProperty("reason").GetString().Should().Be(
            $"No creation policy of order type '{TenantAttachmentRequestOrderTypes.AttachmentRequestId}' admits the caller.");
        (await host.Orders.GetByOrderTypeAsync(TenantAttachmentRequestOrderTypes.AttachmentRequestId)).Should().BeEmpty();
    }

    [Fact]
    public async Task SubmitAttachmentRequest_IspTenant_Returns201()
    {
        await using var host = await StartAsync();
        using var client = PortalClientFor(host, "tenant-a");

        var response = await client.PostAsJsonAsync(
            Path("/api/isp/tenant-portal/attachment-requests"), new SubmitAttachmentRequestRequest("pole-123", "New fiber attachment"));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await host.Orders.GetByOrderTypeAsync(TenantAttachmentRequestOrderTypes.AttachmentRequestId))
            .Should().ContainSingle().Which.OrganizationId.Should().Be(OrderTargetsApiHost.OrgA);
    }
}
