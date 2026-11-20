using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.NeutralOperator.Application;
using GeoAssets.Shared.Services;
using GeoAssets.Web.Services.Isp.Rest;
using GeoAssets.Web.Tests.Identity;
using GeoAssets.Workflow.Rest.Routing;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Web.Tests.Isp;

/// <summary>
/// XD01-384: the tenant portal's REST client resolves the workflow API root of the order type each call is about through
/// <see cref="IOrderApiRoutes"/> (the roots the kernel's registry names on the gateway), and without it keeps requesting
/// relative to its own client's base address.
/// </summary>
public sealed class TenantPortalRoutedClientTests
{
    private static readonly Uri Kernel = new("http://gw/api/workflow/");
    private static readonly Uri Vertical = new("http://gw/api/v/neutral-operator/workflow/");
    private static readonly Uri Other = new("http://gw/api/v/other/workflow/");

    private sealed class Routes(IReadOnlyDictionary<string, Uri> byOrderType) : IOrderApiRoutes
    {
        public List<string> Resolved { get; } = [];

        public Uri DefaultRoot => Kernel;

        public Task<IReadOnlyList<Uri>> GetRootsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Uri>>([Kernel, .. byOrderType.Values.Distinct()]);

        public Task<Uri> ResolveAsync(string orderTypeId, CancellationToken ct)
        {
            Resolved.Add(orderTypeId);
            return Task.FromResult(byOrderType.TryGetValue(orderTypeId, out var root) ? root : Kernel);
        }
    }

    private sealed class Authorization : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) =>
            Task.FromResult(new AuthorizationContext
            {
                User = new AppUser { Id = Guid.NewGuid(), Email = "t@example.com", DisplayName = "T", CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), OrganizationId = Guid.NewGuid() },
                Roles = [],
                Claims = [],
                Permissions = [],
            });
    }

    private static (TenantPortalRestClient Client, FakeHttpMessageHandler Handler) Build(IOrderApiRoutes? routes)
    {
        var handler = new FakeHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(Array.Empty<object>()) });
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://gw/api/workflow/") };
        return (new TenantPortalRestClient(http, new WorkflowPrincipalFactory(new Authorization()), new FakeTimeProvider(), routes), handler);
    }

    private static Routes OnVertical() => new(new Dictionary<string, Uri>(StringComparer.Ordinal)
    {
        [TenantAttachmentRequestOrderTypes.AttachmentRequestId] = Vertical,
        [ClandestineAttachmentOrderTypes.ClandestineNoticeId] = Vertical,
    });

    [Fact]
    public async Task Submit_GoesToTheRootOfTheAttachmentRequestType()
    {
        var routes = OnVertical();
        var (client, handler) = Build(routes);

        await client.SubmitAttachmentRequestAsync("pole-1", "d");

        handler.Requests.Single().RequestUri.Should().Be(new Uri("http://gw/api/v/neutral-operator/workflow/service-orders"));
        routes.Resolved.Should().Equal(TenantAttachmentRequestOrderTypes.AttachmentRequestId);
    }

    [Fact]
    public async Task MyRequests_AreListedFromTheRootOfTheAttachmentRequestType()
    {
        var (client, handler) = Build(OnVertical());

        await client.GetMyAttachmentRequestsAsync();

        handler.Requests.Single().RequestUri.Should().Be(
            new Uri("http://gw/api/v/neutral-operator/workflow/service-orders/by-order-type/isp-tenant-attachment-request"));
    }

    [Fact]
    public async Task FlaggedNotices_AreListedFromTheRootOfTheClandestineNoticeType_QueryKept()
    {
        var (client, handler) = Build(OnVertical());

        await client.GetFlaggedNoticesAsync();

        handler.Requests.Single().RequestUri.Should().Be(
            new Uri("http://gw/api/v/neutral-operator/workflow/service-orders/by-order-type/isp-clandestine-notice?status=CaboARevelia"));
    }

    [Fact]
    public async Task ADispute_GoesToTheRootOfTheClandestineNoticeType_WithTheIdEscaped()
    {
        var (client, handler) = Build(OnVertical());

        await client.SubmitDisputeAsync("a b/c", "evidence");

        handler.Requests.Single().RequestUri!.AbsoluteUri.Should().Be("http://gw/api/v/neutral-operator/workflow/service-orders/a%20b%2Fc/actions");
    }

    [Fact]
    public async Task EachCall_UsesTheRootOfItsOwnOrderType_WhenTheTwoTypesAreServedByDifferentRoots()
    {
        var routes = new Routes(new Dictionary<string, Uri>(StringComparer.Ordinal)
        {
            [TenantAttachmentRequestOrderTypes.AttachmentRequestId] = Vertical,
            [ClandestineAttachmentOrderTypes.ClandestineNoticeId] = Other,
        });
        var (client, handler) = Build(routes);

        await client.GetMyAttachmentRequestsAsync();
        await client.GetFlaggedNoticesAsync();
        await client.SubmitDisputeAsync("n1", "e");

        handler.Requests.Select(r => r.RequestUri!.GetLeftPart(UriPartial.Path)).Should().Equal(
            "http://gw/api/v/neutral-operator/workflow/service-orders/by-order-type/isp-tenant-attachment-request",
            "http://gw/api/v/other/workflow/service-orders/by-order-type/isp-clandestine-notice",
            "http://gw/api/v/other/workflow/service-orders/n1/actions");
    }

    [Fact]
    public async Task WhenNoDescriptorNamesTheTypes_EveryCallGoesToTheKernelsRoot()
    {
        var (client, handler) = Build(new Routes(new Dictionary<string, Uri>(StringComparer.Ordinal)));

        await client.SubmitAttachmentRequestAsync("pole-1", "d");
        await client.GetMyAttachmentRequestsAsync();

        handler.Requests.Select(r => r.RequestUri!.GetLeftPart(UriPartial.Path)).Should().Equal(
            "http://gw/api/workflow/service-orders",
            "http://gw/api/workflow/service-orders/by-order-type/isp-tenant-attachment-request");
    }

    [Fact]
    public async Task WithoutRoutes_EveryCallIsRelativeToTheClientsBaseAddress()
    {
        var (client, handler) = Build(routes: null);

        await client.SubmitAttachmentRequestAsync("pole-1", "d");
        await client.GetFlaggedNoticesAsync();
        await client.SubmitDisputeAsync("n1", "e");

        handler.Requests.Select(r => r.RequestUri!.GetLeftPart(UriPartial.Path)).Should().Equal(
            "http://gw/api/workflow/service-orders",
            "http://gw/api/workflow/service-orders/by-order-type/isp-clandestine-notice",
            "http://gw/api/workflow/service-orders/n1/actions");
    }
}
