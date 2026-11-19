using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Shared.Services;
using GeoAssets.Web.Services.Isp.Rest;
using GeoAssets.Web.Tests.Identity;
using GeoAssets.NeutralOperator.Application;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Web.Tests.Isp;

/// <summary>
/// The tenant portal's REST client over the generic <c>/api/workflow/service-orders</c> routes (XD01-365): the exact request
/// each of its four calls sends, and what it makes of the answer.
/// </summary>
public sealed class TenantPortalRestClientTests
{
    private static readonly Guid UserId = new("00000000-0000-4000-8000-000000000365");
    private static readonly Guid OrganizationId = new("a0a0a0a0-0000-4000-8000-000000000365");
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 10, 30, 0, TimeSpan.Zero);

    private sealed class FakeAuthorizationService(Guid? organizationId) : IGeoAuthorizationService
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
                    Id = UserId,
                    Email = "tenant@example.com",
                    DisplayName = "Tenant",
                    CreatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                    OrganizationId = organizationId,
                },
                Roles = ["IspTenant"],
                Claims = [],
                Permissions = [],
            });
    }

    private static (TenantPortalRestClient Client, FakeHttpMessageHandler Handler) Build(
        Func<HttpRequestMessage, HttpResponseMessage> respond, bool withOrganization = true)
    {
        var handler = new FakeHttpMessageHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://server/api/workflow/") };
        var principals = new WorkflowPrincipalFactory(new FakeAuthorizationService(withOrganization ? OrganizationId : null));
        return (new TenantPortalRestClient(http, principals, new FakeTimeProvider(Now)), handler);
    }

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK);

    private static HttpResponseMessage Orders(params string[] ids) => new(HttpStatusCode.OK)
    {
        Content = JsonContent.Create(ids.Select(id => new { id, orderTypeId = "x", status = "y" })),
    };

    private static async Task<JsonElement> BodyOfAsync(HttpRequestMessage request) =>
        JsonDocument.Parse(await request.Content!.ReadAsStringAsync()).RootElement.Clone();

    // ── Submit ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitAttachmentRequestAsync_PostsTheOrderToServiceOrders()
    {
        var (client, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.Created));

        await client.SubmitAttachmentRequestAsync("pole-123", "New fiber attachment");

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("http://server/api/workflow/service-orders"));
    }

    [Fact]
    public async Task SubmitAttachmentRequestAsync_BodyCarriesTheTypeStatusAttributeCreatorOrganizationAndTime()
    {
        var (client, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.Created));

        var order = await client.SubmitAttachmentRequestAsync("pole-123", "New fiber attachment");

        var body = await BodyOfAsync(handler.Requests.Single());
        body.GetProperty("orderTypeId").GetString().Should().Be(TenantAttachmentRequestOrderTypes.AttachmentRequestId);
        body.GetProperty("status").GetString().Should().Be(TenantAttachmentRequestWorkflowGraph.Submitted);
        body.GetProperty("attributes").GetProperty("poleFeatureId").GetString().Should().Be("pole-123");
        body.GetProperty("createdBy").GetString().Should().Be(UserId.ToString());
        body.GetProperty("organizationId").GetGuid().Should().Be(OrganizationId);
        body.GetProperty("createdAt").GetDateTime().Should().Be(Now.UtcDateTime);
        body.GetProperty("description").GetString().Should().Be("New fiber attachment");
        body.GetProperty("title").GetString().Should().Contain("pole-123");
        order.OrderTypeId.Should().Be(TenantAttachmentRequestOrderTypes.AttachmentRequestId);
        order.Attributes[TenantAttachmentRequestOrderTypes.PoleFeatureIdAttribute].Should().Be("pole-123");
    }

    [Fact]
    public async Task SubmitAttachmentRequestAsync_UserWithoutOrganization_SendsTheEmptyGuid()
    {
        var (client, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.Created), withOrganization: false);

        await client.SubmitAttachmentRequestAsync("pole-123", "d");

        (await BodyOfAsync(handler.Requests.Single())).GetProperty("organizationId").GetGuid().Should().Be(Guid.Empty);
    }

    [Fact]
    public Task SubmitAttachmentRequestAsync_NonSuccess_ThrowsHttpRequestException()
    {
        var (client, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));

        var act = () => client.SubmitAttachmentRequestAsync("pole-123", "d");

        return act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── Lists ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyAttachmentRequestsAsync_GetsTheAttachmentRequestTypesOrders()
    {
        var (client, handler) = Build(_ => Orders("r-1", "r-2"));

        var result = await client.GetMyAttachmentRequestsAsync();

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri.Should().Be(new Uri("http://server/api/workflow/service-orders/by-order-type/isp-tenant-attachment-request"));
        result.Select(o => o.Id).Should().Equal("r-1", "r-2");
    }

    [Fact]
    public async Task GetFlaggedNoticesAsync_GetsTheNoticeTypesOrdersInCaboARevelia()
    {
        var (client, handler) = Build(_ => Orders("n-1"));

        var result = await client.GetFlaggedNoticesAsync();

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri.Should().Be(
            new Uri("http://server/api/workflow/service-orders/by-order-type/isp-clandestine-notice?status=CaboARevelia"));
        result.Select(o => o.Id).Should().Equal("n-1");
    }

    [Fact]
    public async Task GetMyAttachmentRequestsAsync_EmptyList_ReturnsEmpty()
    {
        var (client, _) = Build(_ => Orders());

        (await client.GetMyAttachmentRequestsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task GetFlaggedNoticesAsync_NullBody_ReturnsEmpty()
    {
        var (client, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create<object?>(null) });

        (await client.GetFlaggedNoticesAsync()).Should().BeEmpty();
    }

    [Fact]
    public Task GetFlaggedNoticesAsync_NonSuccess_ThrowsHttpRequestException()
    {
        var (client, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var act = () => client.GetFlaggedNoticesAsync();

        return act.Should().ThrowAsync<HttpRequestException>();
    }

    // ── Dispute ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task SubmitDisputeAsync_PostsTheActionToTheOrdersActionsRoute()
    {
        var (client, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.SubmitDisputeAsync("notice-1", "receipt-digest-abc123");

        var request = handler.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri("http://server/api/workflow/service-orders/notice-1/actions"));
    }

    [Fact]
    public async Task SubmitDisputeAsync_EscapesTheOrderIdInThePath()
    {
        var (client, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.SubmitDisputeAsync("a b/c?d", "evidence");

        handler.Requests.Single().RequestUri!.AbsoluteUri.Should().Be("http://server/api/workflow/service-orders/a%20b%2Fc%3Fd/actions");
    }

    [Fact]
    public async Task SubmitDisputeAsync_BodyIsADisputeActionWithTheEvidenceAsItsCommentAndTheDisputedStatus()
    {
        var (client, handler) = Build(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await client.SubmitDisputeAsync("notice-1", "receipt-digest-abc123");

        // GeoJsonSerializer.Options has no enum converter, so the action travels as its number.
        var body = await BodyOfAsync(handler.Requests.Single());
        body.GetProperty("action").GetInt32().Should().Be(OrderActionAliases.NumberOf(NeutralOperatorActions.Dispute));
        body.GetProperty("comment").GetString().Should().Be("receipt-digest-abc123");
        body.GetProperty("resultingStatus").GetString().Should().Be("Disputed");
        body.GetProperty("performedBy").GetString().Should().Be(UserId.ToString());
        body.GetProperty("performedAt").GetDateTime().Should().Be(Now.UtcDateTime);
    }

    [Fact]
    public Task SubmitDisputeAsync_NonSuccess_ThrowsHttpRequestException()
    {
        var (client, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var act = () => client.SubmitDisputeAsync("notice-1", "evidence");

        return act.Should().ThrowAsync<HttpRequestException>();
    }
}
