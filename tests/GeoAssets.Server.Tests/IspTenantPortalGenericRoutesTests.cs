using System.Net;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// The ISP tenant portal over the generic <c>/api/workflow/service-orders</c> routes (XD01-365): the bespoke
/// <c>/api/isp/tenant-portal</c> routes are gone, so the portal lists by order type (optionally by status), creates with
/// <c>POST service-orders</c> and disputes with <c>POST service-orders/{id}/actions</c>. Tenant isolation, formerly proved
/// on the portal routes, is proved here on the routes that replaced them.
/// </summary>
public sealed class IspTenantPortalGenericRoutesTests
{
    private const string Evidence = "receipt-digest-abc123";

    private static Task<OrderTargetsApiHost> StartAsync(bool validating = false) =>
        OrderTargetsApiHost.StartAsync(
            mapOrderRoutes: true,
            withIspOrderTypes: true,
            decorateOrders: validating ? WithAttributeValidation : null);

    private static ValidatingServiceOrderRepository WithAttributeValidation(IServiceOrderRepository inner)
    {
        var registry = new OrderTypeRegistry();
        ClandestineAttachmentOrderTypes.RegisterAll(registry);
        TenantAttachmentRequestOrderTypes.RegisterAll(registry);
        return new ValidatingServiceOrderRepository(inner, registry);
    }

    private static string[] Ids(JsonElement list) => [.. list.EnumerateArray().Select(e => e.GetProperty("id").GetString()!)];

    private static Task<ServiceOrder> SeedNoticeAsync(OrderTargetsApiHost host, string id, string status, Guid organizationId) =>
        host.SeedOrderAsync(id, ClandestineAttachmentOrderTypes.ClandestineNoticeId, status, organizationId);

    private static Task<HttpResponseMessage> CreateRequestAsync(HttpClient client, string id, object? attributes) =>
        client.PostJsonAsync("service-orders", new
        {
            id,
            title = "Attachment request",
            orderTypeId = TenantAttachmentRequestOrderTypes.AttachmentRequestId,
            status = TenantAttachmentRequestWorkflowGraph.Submitted,
            attributes,
        });

    private static string NoticesByStatus(string status) =>
        $"service-orders/by-order-type/{ClandestineAttachmentOrderTypes.ClandestineNoticeId}?status={status}";

    private const string AllNotices = $"service-orders/by-order-type/{ClandestineAttachmentOrderTypes.ClandestineNoticeId}";

    // ── Tenant isolation on the generic routes ────────────────────────────────

    [Fact]
    public async Task ListAttachmentRequests_ReturnsOnlyCallersOwnOrganizationsRequests()
    {
        await using var host = await StartAsync();
        await host.SeedOrderAsync("mine", TenantAttachmentRequestOrderTypes.AttachmentRequestId, TenantAttachmentRequestWorkflowGraph.Submitted, OrderTargetsApiHost.OrgA);
        await host.SeedOrderAsync("competitors", TenantAttachmentRequestOrderTypes.AttachmentRequestId, TenantAttachmentRequestWorkflowGraph.Submitted, OrderTargetsApiHost.OrgB);
        using var client = host.ClientFor("tenant-a");

        var response = await client.GetAsync(Path($"service-orders/by-order-type/{TenantAttachmentRequestOrderTypes.AttachmentRequestId}"));

        Ids(await response.ReadJsonAsync()).Should().BeEquivalentTo(["mine"], "a tenant must never see a competitor's attachment requests");
    }

    [Fact]
    public async Task GetAttachmentRequest_DifferentOrganization_Returns404()
    {
        await using var host = await StartAsync();
        await host.SeedOrderAsync("competitors", TenantAttachmentRequestOrderTypes.AttachmentRequestId, TenantAttachmentRequestWorkflowGraph.Submitted, OrderTargetsApiHost.OrgB);
        using var client = host.ClientFor("tenant-a");

        // Direct-ID-manipulation attempt: guessing another tenant's order id.
        var response = await client.GetAsync(Path("service-orders/competitors"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetAttachmentRequest_OwnOrganization_Returns200()
    {
        await using var host = await StartAsync();
        await host.SeedOrderAsync("mine", TenantAttachmentRequestOrderTypes.AttachmentRequestId, TenantAttachmentRequestWorkflowGraph.Submitted, OrderTargetsApiHost.OrgA);
        using var client = host.ClientFor("tenant-a");

        var response = await client.GetAsync(Path("service-orders/mine"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ListFlaggedNotices_ReturnsOnlyCallersOwnOrganizationsFlags()
    {
        await using var host = await StartAsync();
        await SeedNoticeAsync(host, "mine", ClandestineAttachmentWorkflowGraph.CaboARevelia, OrderTargetsApiHost.OrgA);
        await SeedNoticeAsync(host, "competitors", ClandestineAttachmentWorkflowGraph.CaboARevelia, OrderTargetsApiHost.OrgB);
        using var client = host.ClientFor("tenant-a");

        var response = await client.GetAsync(Path(NoticesByStatus(ClandestineAttachmentWorkflowGraph.CaboARevelia)));

        Ids(await response.ReadJsonAsync()).Should().BeEquivalentTo(["mine"], "a tenant must never see a competitor's flagged notices");
    }

    [Fact]
    public async Task SubmitAttachmentRequest_CreatesOrderScopedToCallersOrganization()
    {
        await using var host = await StartAsync();
        using var client = host.ClientFor("tenant-a");

        var response = await CreateRequestAsync(client, "request-1", new { poleFeatureId = "pole-123" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var stored = (await host.Orders.GetByIdAsync("request-1"))!;
        stored.OrganizationId.Should().Be(OrderTargetsApiHost.OrgA);
        stored.Status.Should().Be(TenantAttachmentRequestWorkflowGraph.Submitted);
    }

    [Fact]
    public async Task Dispute_DifferentOrganizationsNotice_Returns404_AndDoesNotTransition()
    {
        await using var host = await StartAsync();
        await SeedNoticeAsync(host, "competitors-notice", ClandestineAttachmentWorkflowGraph.CaboARevelia, OrderTargetsApiHost.OrgB);
        using var client = host.ClientFor("tenant-a");

        // Direct-ID-manipulation attempt against a notice that isn't the caller's.
        var response = await client.PostJsonAsync(
            "service-orders/competitors-notice/actions",
            new
            {
                action = (int)OrderActionType.Dispute,
                performedBy = "whoever-the-body-says",
                performedAt = OrderTargetsApiHost.Now.UtcDateTime,
                comment = Evidence,
                resultingStatus = ClandestineAttachmentWorkflowGraph.Disputed,
            });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await host.Orders.GetByIdAsync("competitors-notice"))!.Status.Should().Be(
            ClandestineAttachmentWorkflowGraph.CaboARevelia, "a denied dispute attempt must not transition the order");
    }

    // ── by-order-type ?status= ────────────────────────────────────────────────

    [Fact]
    public async Task ByOrderType_StatusFilter_ReturnsOnlyOrdersInThatStatus()
    {
        await using var host = await StartAsync();
        await SeedNoticeAsync(host, "active", ClandestineAttachmentWorkflowGraph.Active, OrderTargetsApiHost.OrgA);
        await SeedNoticeAsync(host, "flagged", ClandestineAttachmentWorkflowGraph.CaboARevelia, OrderTargetsApiHost.OrgA);
        await SeedNoticeAsync(host, "disputed", ClandestineAttachmentWorkflowGraph.Disputed, OrderTargetsApiHost.OrgA);
        using var client = host.ClientFor("tenant-a");

        var response = await client.GetAsync(Path(NoticesByStatus(ClandestineAttachmentWorkflowGraph.CaboARevelia)));

        Ids(await response.ReadJsonAsync()).Should().Equal("flagged");
    }

    [Fact]
    public async Task ByOrderType_NoStatus_ReturnsAllReadable()
    {
        await using var host = await StartAsync();
        await SeedNoticeAsync(host, "active", ClandestineAttachmentWorkflowGraph.Active, OrderTargetsApiHost.OrgA);
        await SeedNoticeAsync(host, "flagged", ClandestineAttachmentWorkflowGraph.CaboARevelia, OrderTargetsApiHost.OrgA);
        await SeedNoticeAsync(host, "disputed", ClandestineAttachmentWorkflowGraph.Disputed, OrderTargetsApiHost.OrgA);
        await SeedNoticeAsync(host, "other-orgs", ClandestineAttachmentWorkflowGraph.Active, OrderTargetsApiHost.OrgB);
        using var client = host.ClientFor("tenant-a");

        var response = await client.GetAsync(Path(AllNotices));

        Ids(await response.ReadJsonAsync()).Should().BeEquivalentTo(["active", "flagged", "disputed"]);
    }

    [Fact]
    public async Task ByOrderType_StatusFilter_StillAppliesTheOrganizationGate()
    {
        await using var host = await StartAsync();
        await SeedNoticeAsync(host, "mine", ClandestineAttachmentWorkflowGraph.CaboARevelia, OrderTargetsApiHost.OrgA);
        await SeedNoticeAsync(host, "other-orgs", ClandestineAttachmentWorkflowGraph.CaboARevelia, OrderTargetsApiHost.OrgB);
        using var client = host.ClientFor("tenant-a");

        var response = await client.GetAsync(Path(NoticesByStatus(ClandestineAttachmentWorkflowGraph.CaboARevelia)));

        Ids(await response.ReadJsonAsync()).Should().Equal("mine").And.NotContain("other-orgs");
    }

    // ── The attachment request's attribute schema ─────────────────────────────

    [Fact]
    public async Task CreateAttachmentRequest_WithoutPoleFeatureId_Returns400WithSchemaErrors()
    {
        await using var host = await StartAsync(validating: true);
        using var client = host.ClientFor("tenant-a");

        var response = await CreateRequestAsync(client, "request-1", new { });

        var body = await response.ReadJsonAsync(HttpStatusCode.BadRequest);
        body.GetProperty("orderTypeId").GetString().Should().Be(TenantAttachmentRequestOrderTypes.AttachmentRequestId);
        body.GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);
        (await host.Orders.GetByIdAsync("request-1")).Should().BeNull();
    }

    [Fact]
    public async Task CreateAttachmentRequest_NumericLookingPoleFeatureId_IsAccepted()
    {
        await using var host = await StartAsync(validating: true);
        using var client = host.ClientFor("tenant-a");

        var response = await CreateRequestAsync(client, "request-1", new { poleFeatureId = "12345" });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        (await host.Orders.GetByIdAsync("request-1")).Should().NotBeNull();
    }

    // ── The journey ───────────────────────────────────────────────────────────

    [Fact]
    public async Task TenantPortalJourney_CreateListFlaggedDispute_ThroughGenericRoutes()
    {
        await using var host = await StartAsync(validating: true);
        await SeedNoticeAsync(host, "notice-1", ClandestineAttachmentWorkflowGraph.Active, OrderTargetsApiHost.OrgA);
        using var tenant = host.ClientFor("tenant-a");
        using var supervisor = host.ClientFor("supervisor-a");

        // The tenant submits a request and finds it in its list.
        (await CreateRequestAsync(tenant, "request-1", new { poleFeatureId = "pole-123" })).StatusCode.Should().Be(HttpStatusCode.Created);
        var mine = await tenant.GetAsync(Path($"service-orders/by-order-type/{TenantAttachmentRequestOrderTypes.AttachmentRequestId}"));
        Ids(await mine.ReadJsonAsync()).Should().Equal("request-1");

        // Nothing is flagged yet; then the operator's supervisor flags the notice through the generic actions route.
        Ids(await (await tenant.GetAsync(Path(NoticesByStatus(ClandestineAttachmentWorkflowGraph.CaboARevelia)))).ReadJsonAsync()).Should().BeEmpty();
        var flag = await supervisor.PostJsonAsync(
            "service-orders/notice-1/actions",
            new
            {
                action = (int)OrderActionType.FlagClandestine,
                performedBy = "supervisor",
                performedAt = OrderTargetsApiHost.Now.UtcDateTime,
                comment = "found on pole",
                resultingStatus = ClandestineAttachmentWorkflowGraph.CaboARevelia,
            });
        flag.StatusCode.Should().Be(HttpStatusCode.NoContent, await flag.Content.ReadAsStringAsync());

        // The tenant sees it flagged, disputes it, and it leaves the flagged list.
        var flagged = await tenant.GetAsync(Path(NoticesByStatus(ClandestineAttachmentWorkflowGraph.CaboARevelia)));
        Ids(await flagged.ReadJsonAsync()).Should().Equal("notice-1");

        var dispute = await tenant.PostJsonAsync(
            "service-orders/notice-1/actions",
            new
            {
                action = (int)OrderActionType.Dispute,
                performedBy = "tenant",
                performedAt = OrderTargetsApiHost.Now.UtcDateTime,
                comment = Evidence,
                resultingStatus = ClandestineAttachmentWorkflowGraph.Disputed,
            });
        dispute.StatusCode.Should().Be(HttpStatusCode.NoContent, await dispute.Content.ReadAsStringAsync());

        var after = await tenant.GetAsync(Path(NoticesByStatus(ClandestineAttachmentWorkflowGraph.CaboARevelia)));
        Ids(await after.ReadJsonAsync()).Should().BeEmpty();
        (await host.Orders.GetByIdAsync("notice-1"))!.Status.Should().Be(ClandestineAttachmentWorkflowGraph.Disputed);
    }
}
