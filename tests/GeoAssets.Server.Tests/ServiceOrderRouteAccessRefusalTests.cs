using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Server.Modules.NeutralOperator;
using GeoAssets.Workflow.Isp.NeutralOperator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// How a route answers a failed service-order requirement (XD01-364): <c>ServiceOrderRouteAccess.Refusal(AuthorizationResult)</c>
/// is internal to the server, so each of its answers is reached through the tenant portal's route that evaluates the
/// requirement — a verdict outside the organization is a 404, a verdict from a rule is a 403 with the engine's reason, a
/// refusal with only a message is a 403 with that message, and a failure that names no reason at all is a 403 "Not authorized.".
/// </summary>
public sealed class ServiceOrderRouteAccessRefusalTests
{
    private static HttpClient PortalClientFor(OrderTargetsApiHost host, string token)
    {
        var client = host.Server.CreateClient();
        client.DefaultRequestHeaders.Add(OrderTargetsApiHost.TokenHeader, token);
        return client;
    }

    private static async Task SeedFlaggedNoticeAsync(OrderTargetsApiHost host)
    {
        await host.SeedOrderAsync(
            "notice-1", ClandestineAttachmentOrderTypes.ClandestineNoticeId, ClandestineAttachmentWorkflowGraph.Active, OrderTargetsApiHost.OrgA);
        await new ClandestineAttachmentService(host.Orders, host.Clock).FlagAsync("notice-1");
    }

    private static Task<HttpResponseMessage> DisputeAsync(HttpClient client) =>
        client.PostAsJsonAsync(
            Path("/api/isp/tenant-portal/flagged-notices/notice-1/dispute"), new SubmitDisputeRequest("receipt-digest-abc123"));

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client) =>
        client.PostAsJsonAsync(
            Path("/api/isp/tenant-portal/attachment-requests"), new SubmitAttachmentRequestRequest("pole-123", "New fiber attachment"));

    [Fact]
    public async Task Refusal_VerdictOutsideTheOrganization_Is404()
    {
        await using var host = await OrderTargetsApiHost.StartAsync(mapIspPortalRoutes: true);
        await SeedFlaggedNoticeAsync(host);
        using var client = PortalClientFor(host, "tenant-b");

        var response = await DisputeAsync(client);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Refusal_VerdictFromARule_Is403WithTheEnginesReason()
    {
        await using var host = await OrderTargetsApiHost.StartAsync(mapIspPortalRoutes: true);
        await SeedFlaggedNoticeAsync(host);
        using var client = PortalClientFor(host, "create-only-a");

        var response = await DisputeAsync(client);

        var body = await response.ReadJsonAsync(HttpStatusCode.Forbidden);
        body.GetProperty("reason").GetString().Should().Be("Denied by rule 'OrderTypeActionPermissionRule'.");
    }

    [Fact]
    public async Task Refusal_MessageOnly_Is403WithThatMessage()
    {
        await using var host = await OrderTargetsApiHost.StartAsync(mapIspPortalRoutes: true);
        using var client = PortalClientFor(host, "create-only-a");

        var response = await SubmitAsync(client);

        var body = await response.ReadJsonAsync(HttpStatusCode.Forbidden);
        body.GetProperty("reason").GetString().Should().Be(
            $"No creation policy of order type '{TenantAttachmentRequestOrderTypes.AttachmentRequestId}' admits the caller.");
    }

    [Fact]
    public async Task Refusal_NoReasonAtAll_Is403NotAuthorized()
    {
        // No handler answers the create requirement, so it fails without a reason of its own.
        await using var host = await OrderTargetsApiHost.StartAsync(
            mapIspPortalRoutes: true,
            configureServices: services => services.Remove(services.Single(d =>
                d.ServiceType == typeof(IAuthorizationHandler) && d.ImplementationType == typeof(ServiceOrderCreateAuthorizationHandler))));
        using var client = PortalClientFor(host, "tenant-a");

        var response = await SubmitAsync(client);

        var body = await response.ReadJsonAsync(HttpStatusCode.Forbidden);
        body.GetProperty("reason").GetString().Should().Be("Not authorized.");
    }
}
