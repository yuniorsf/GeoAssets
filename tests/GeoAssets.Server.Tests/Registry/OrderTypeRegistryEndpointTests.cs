using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Interfaces;
using GeoAssets.Core.Models;
using GeoAssets.Core.Models.Geometry;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Registry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace GeoAssets.Server.Tests.Registry;

/// <summary>
/// The kernel's order-type registry routes (XD01-369) over the real <c>MapOrderTypeRegistryApi()</c> in a
/// <see cref="TestServer"/>, with the production permission bridge and service-order authorization, the shared-use
/// order types and descriptor the NeutralOperator module publishes, and a pole and a plain feature. A request's caller
/// is named by its <c>X-Test-*</c> headers.
/// </summary>
public sealed class OrderTypeRegistryEndpointTests
{
    private const string PoleFeatureId = "pole-1";
    private const string PlainFeatureId = "plain-1";
    private const string AuthScheme = "RegistryTest";

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);
    private static readonly Uri OrderTypesUri = new("/api/registry/order-types", UriKind.Relative);
    private static readonly Uri PublishUri = new("/api/registry/verticals/vertical-a/order-types", UriKind.Relative);

    // Equal to NeutralOperatorModule.SharedUseDescriptor (internal to the Server assembly);
    // NeutralOperatorModuleTests proves the module publishes a source equal to it.
    private static readonly OrderTypeDescriptor SharedUseDescriptor = new()
    {
        VerticalId = "neutral-operator",
        OrderTypeId = SharedUseOrderTypes.PoleWorkId,
        DisplayNameKey = "orderTypes.sharedUsePoleWork",
        ApiBaseRoute = "/api/workflow/service-orders",
        FeatureActions =
        [
            new FeatureActionDescriptor(
                "occupiable-support", "create", "featureActions.createSharedUseOrder", SharedUseOrderTypes.PoleWorkId, "/api/workflow/service-orders"),
        ],
    };

    private static readonly string[] SupervisorPermissions = ["features:read", "serviceorders:read"];

    private sealed class HeaderAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("X-Test-User")) return Task.FromResult(AuthenticateResult.NoResult());

            List<Claim> claims = [new(ClaimTypes.NameIdentifier, "registry-user")];
            claims.AddRange(Split("X-Test-Roles").Select(r => new Claim(ClaimTypes.Role, r)));
            claims.AddRange(Split("X-Test-Permissions").Select(p => new Claim("perm", p)));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, AuthScheme));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, AuthScheme)));
        }

        private string[] Split(string header) =>
            Request.Headers.TryGetValue(header, out var value)
                ? value.ToString().Split(',', StringSplitOptions.RemoveEmptyEntries)
                : [];
    }

    private sealed class ClaimsAuthorizationService(IHttpContextAccessor http) : IGeoAuthorizationService
    {
        private ClaimsPrincipal User => http.HttpContext!.User;

        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) =>
            Task.FromResult(User.HasClaim("perm", permissionCode));

        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) =>
            Task.FromResult(new AuthorizationContext
            {
                User = new AppUser
                {
                    Id = Guid.NewGuid(),
                    Email = "user@example.com",
                    DisplayName = "User",
                    CreatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
                    OrganizationId = Guid.NewGuid(),
                },
                Roles = [.. User.FindAll(ClaimTypes.Role).Select(c => c.Value)],
                Claims = [],
                Permissions = [.. User.FindAll("perm").Select(c => c.Value)],
            });

        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static async Task<TestServer> BuildServerAsync()
    {
        var polesType = new AssetType { Id = Guid.NewGuid(), Name = "Pole", Roles = ["occupiable-support"] };
        var plainType = new AssetType { Id = Guid.NewGuid(), Name = "Plain" };
        var assets = new TestAssetProvider();
        assets.AddAssetType(polesType);
        assets.AddAssetType(plainType);
        assets.Add(Feature(PoleFeatureId, polesType));
        assets.Add(Feature(PlainFeatureId, plainType));

        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddSingleton<IAssetProvider>(assets);
                    services.AddOrderTypeDescriptorRegistry();
                    services.AddOrderTypeDescriptors("neutral-operator", [SharedUseDescriptor]);
                    services.AddOrderTypeRegistry(registry => SharedUseOrderTypes.RegisterAll(registry));
                    services.AddServiceOrderRules();
                    services.AddScoped<ServerWorkflowPrincipalFactory>();
                    services.AddServiceOrderAuthorization();
                    services.AddSingleton<IOrganizationGrantRepository, TestOrganizationGrantRepository>();
                    services.AddSingleton<IGeoAuthorizationService, ClaimsAuthorizationService>();
                    services.AddAuthentication(AuthScheme).AddScheme<AuthenticationSchemeOptions, HeaderAuthenticationHandler>(AuthScheme, _ => { });
                    services.AddAuthorization();
                    services.AddGeoAuthorizationPolicyBridge();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapOrderTypeRegistryApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private static GeoFeature Feature(string id, AssetType type) => new()
    {
        Id = id,
        Geometry = new GeoPoint(1, 1),
        Properties = new GeoFeatureProperties { Name = id, AssetTypeId = type.Id.ToString() },
    };

    private static HttpClient Caller(TestServer server, string[] roles, string[] permissions, string[]? appRoles = null)
    {
        var client = server.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "1");
        client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(',', [.. roles, .. appRoles ?? []]));
        client.DefaultRequestHeaders.Add("X-Test-Permissions", string.Join(',', permissions));
        return client;
    }

    private static HttpClient Supervisor(TestServer server) => Caller(server, ["Supervisor"], SupervisorPermissions);

    private static HttpClient Publisher(TestServer server) => Caller(server, ["Administrator"], ["registry:manage", "serviceorders:read"]);

    private static Uri ActionsUri(string featureId) => new($"/api/geoassets/features/{featureId}/actions", UriKind.Relative);

    private static OrderTypeDescriptor VerticalA(string orderTypeId = "a-1") => new()
    {
        VerticalId = "vertical-a",
        OrderTypeId = orderTypeId,
        DisplayNameKey = "orderTypes.a",
        ApiBaseRoute = "/api/a",
    };

    private static async Task<FeatureActionDescriptor[]> ActionsAsync(HttpClient client, string featureId)
    {
        var response = await client.GetAsync(ActionsUri(featureId));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<FeatureActionDescriptor[]>(Web))!;
    }

    [Fact]
    public async Task FeatureActions_PoleFeature_OffersCreateSharedUseOrder()
    {
        using var server = await BuildServerAsync();
        using var client = Supervisor(server);

        var actions = await ActionsAsync(client, PoleFeatureId);

        actions.Should().ContainSingle().Which.Should().Be(SharedUseDescriptor.FeatureActions[0]);
    }

    [Fact]
    public async Task FeatureActions_NonPoleFeature_OffersNothing()
    {
        using var server = await BuildServerAsync();
        using var client = Supervisor(server);

        var actions = await ActionsAsync(client, PlainFeatureId);

        actions.Should().BeEmpty();
    }

    [Fact]
    public async Task FeatureActions_CallerWithoutCreateRights_DoesNotSeeTheAction()
    {
        using var server = await BuildServerAsync();
        using var client = Caller(server, ["FieldTechnician"], SupervisorPermissions);

        var actions = await ActionsAsync(client, PoleFeatureId);

        actions.Should().BeEmpty();
    }

    [Fact]
    public async Task FeatureActions_AdministratorCaller_SeesTheAction()
    {
        using var server = await BuildServerAsync();
        using var client = Caller(server, ["Administrator"], SupervisorPermissions);

        var actions = await ActionsAsync(client, PoleFeatureId);

        actions.Should().ContainSingle();
    }

    [Fact]
    public async Task FeatureActions_UnknownFeature_Returns404()
    {
        using var server = await BuildServerAsync();
        using var client = Supervisor(server);

        var response = await client.GetAsync(ActionsUri("missing"));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FeatureActions_WithoutFeaturesRead_Returns403()
    {
        using var server = await BuildServerAsync();
        using var client = Caller(server, ["Supervisor"], ["serviceorders:read"]);

        var response = await client.GetAsync(ActionsUri(PoleFeatureId));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task FeatureActions_Anonymous_Returns401()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.GetAsync(ActionsUri(PoleFeatureId));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOrderTypes_ListsThePublishedDescriptors()
    {
        using var server = await BuildServerAsync();
        using var client = Supervisor(server);

        var descriptors = await client.GetFromJsonAsync<OrderTypeDescriptor[]>(OrderTypesUri, Web);

        descriptors.Should().ContainSingle().Which.SameAs(SharedUseDescriptor).Should().BeTrue();
    }

    [Fact]
    public async Task GetOrderTypes_Anonymous_Returns401()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.GetAsync(OrderTypesUri);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetOrderTypes_WithoutServiceOrdersRead_Returns403()
    {
        using var server = await BuildServerAsync();
        using var client = Caller(server, [], ["features:read"]);

        var response = await client.GetAsync(OrderTypesUri);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Publish_NewVertical_Returns200ChangedAndListsIt()
    {
        using var server = await BuildServerAsync();
        using var publisher = Publisher(server);

        var response = await publisher.PutAsJsonAsync(PublishUri, new[] { VerticalA() }, Web);
        var listed = await publisher.GetFromJsonAsync<OrderTypeDescriptor[]>(OrderTypesUri, Web);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"verticalId\":\"vertical-a\"").And.Contain("\"changed\":true");
        listed!.Select(d => d.OrderTypeId).Should().Equal(SharedUseOrderTypes.PoleWorkId, "a-1");
    }

    [Fact]
    public async Task Publish_SamePayloadTwice_SecondReportsNoChange()
    {
        using var server = await BuildServerAsync();
        using var publisher = Publisher(server);

        var first = await publisher.PutAsJsonAsync(PublishUri, new[] { VerticalA() }, Web);
        var second = await publisher.PutAsJsonAsync(PublishUri, new[] { VerticalA() }, Web);
        var listed = await publisher.GetFromJsonAsync<OrderTypeDescriptor[]>(OrderTypesUri, Web);

        (await first.Content.ReadAsStringAsync()).Should().Contain("\"changed\":true");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).Should().Contain("\"changed\":false");
        listed.Should().HaveCount(2);
    }

    [Fact]
    public async Task Publish_WithoutAnEntry_RemovesIt()
    {
        using var server = await BuildServerAsync();
        using var publisher = Publisher(server);
        await publisher.PutAsJsonAsync(PublishUri, new[] { VerticalA("a-1"), VerticalA("a-2") }, Web);

        var response = await publisher.PutAsJsonAsync(PublishUri, new[] { VerticalA("a-2") }, Web);
        var listed = await publisher.GetFromJsonAsync<OrderTypeDescriptor[]>(OrderTypesUri, Web);

        (await response.Content.ReadAsStringAsync()).Should().Contain("\"changed\":true");
        listed!.Select(d => d.OrderTypeId).Should().Equal(SharedUseOrderTypes.PoleWorkId, "a-2");
    }

    [Fact]
    public async Task Publish_WithoutRegistryManage_Returns403AndChangesNothing()
    {
        using var server = await BuildServerAsync();
        using var client = Supervisor(server);

        var response = await client.PutAsJsonAsync(PublishUri, new[] { VerticalA() }, Web);
        var listed = await client.GetFromJsonAsync<OrderTypeDescriptor[]>(OrderTypesUri, Web);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        listed.Should().ContainSingle();
    }

    [Fact]
    public async Task Publish_Anonymous_Returns401()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.PutAsJsonAsync(PublishUri, new[] { VerticalA() }, Web);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Publish_ServiceCallerAppRole_IsAllowedWithoutThePermission()
    {
        using var server = await BuildServerAsync();
        using var client = Caller(server, [], [], [GeoAuthorizationHandler.ServiceCallerAppRole]);

        var response = await client.PutAsJsonAsync(PublishUri, new[] { VerticalA() }, Web);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Publish_MalformedJson_Returns400WithAnError()
    {
        using var server = await BuildServerAsync();
        using var publisher = Publisher(server);
        using var body = new StringContent("{ not json", Encoding.UTF8, "application/json");

        var response = await publisher.PutAsync(PublishUri, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"error\"");
    }

    [Fact]
    public async Task Publish_MismatchedVerticalId_Returns400AndChangesNothing()
    {
        using var server = await BuildServerAsync();
        using var publisher = Publisher(server);
        var mismatched = VerticalA() with { VerticalId = "vertical-b" };

        var response = await publisher.PutAsJsonAsync(PublishUri, new[] { mismatched }, Web);
        var listed = await publisher.GetFromJsonAsync<OrderTypeDescriptor[]>(OrderTypesUri, Web);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"error\"").And.Contain("vertical-b");
        listed.Should().ContainSingle();
    }

    [Fact]
    public async Task Publish_NullBody_Returns400()
    {
        using var server = await BuildServerAsync();
        using var publisher = Publisher(server);
        using var body = new StringContent("null", Encoding.UTF8, "application/json");

        var response = await publisher.PutAsync(PublishUri, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
