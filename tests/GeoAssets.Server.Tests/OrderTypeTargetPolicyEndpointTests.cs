using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Proves <see cref="OrderType.TargetPolicy"/> and <see cref="OrderType.AttributesSchemaJson"/>
/// round-trip through the real production <c>/api/workflow/order-types</c> routes (XD01-304),
/// using the same serializer options <c>RestOrderTypeRepository</c> sends and reads with, and
/// that an invalid policy is rejected before it reaches the repository. Same TestServer setup
/// as <see cref="OrderTypeSchemaValidationEndpointTests"/>.
/// </summary>
public class OrderTypeTargetPolicyEndpointTests
{
    private static readonly Uri OrderTypesUri = new("/api/workflow/order-types", UriKind.Relative);
    private static readonly Uri PoleInspectionUri = new("/api/workflow/order-types/pole-inspection", UriKind.Relative);

    private static readonly JsonSerializerOptions ClientOptions = GeoJsonSerializer.Options;

    private sealed class NeverCalledAuthorizationService : IGeoAuthorizationService
    {
        public Task<bool> IsInRoleAsync(string roleName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasClaimAsync(string claimType, string? claimValue = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(string permissionCode, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(string policyName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> EvaluatePolicyAsync(AppPolicy policy, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<AuthorizationContext> GetAuthorizationContextAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    private static async Task<TestServer> BuildServerAsync()
    {
        var host = await new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost.UseTestServer();
                webHost.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddOrderTypeRegistry();
                    services.AddSingleton<IOrderTypeRepository, FakeOrderTypeRepository>();
                    services.AddSingleton<IServiceOrderRepository>(sp =>
                        new ValidatingServiceOrderRepository(new FakeServiceOrderRepository(), sp.GetService<OrderTypeRegistry>()));
                    services.AddServiceOrderRules();
                    services.AddScoped<ServerWorkflowPrincipalFactory>();
                    services.AddSingleton<IGeoAuthorizationService, NeverCalledAuthorizationService>();
                    services.AddSingleton<IOrganizationGrantRepository, NeverCalledOrganizationGrantRepository>();
                });
                webHost.Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapServiceOrdersApi());
                });
            })
            .StartAsync();

        return host.GetTestServer();
    }

    private static OrderTargetPolicy FullPolicy() => new()
    {
        TargetAssetTypeIds = ["pole", "manhole"],
        AliasAttributeKey = "tag",
        SelectionLimit = 500,
        Activities = [new("inspect", "Inspect"), new("replace", "Replace")],
        Exclusivity = OrderTargetExclusivity.BlockOnOverlap,
        FanOut = new OrderTargetFanOutPolicy("pole-inspection-notice", OrderTargetFanOutPartyKind.OccupantOrganization),
    };

    private static OrderType PoleInspection(OrderTargetPolicy? policy) => new()
    {
        Id = "pole-inspection",
        DisplayName = "Pole inspection",
        AttributesSchemaJson = """{"type":"object"}""",
        TargetPolicy = policy,
    };

    [Fact]
    public async Task Post_ThenGetById_RoundTripsTargetPolicyAndAttributesSchemaJson()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var created = await client.PostAsJsonAsync(OrderTypesUri, PoleInspection(FullPolicy()), ClientOptions);
        var loaded = await client.GetFromJsonAsync<OrderType>(PoleInspectionUri, ClientOptions);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        loaded.Should().BeEquivalentTo(PoleInspection(FullPolicy()));
    }

    [Fact]
    public async Task Post_ThenGetAll_ReturnsTargetPolicy()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        await client.PostAsJsonAsync(OrderTypesUri, PoleInspection(FullPolicy()), ClientOptions);
        var all = await client.GetFromJsonAsync<OrderType[]>(OrderTypesUri, ClientOptions);

        all.Should().ContainSingle().Which.TargetPolicy.Should().BeEquivalentTo(FullPolicy());
    }

    [Fact]
    public async Task Post_TypeWithoutTargetPolicy_IsStoredWithNullPolicy()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var created = await client.PostAsJsonAsync(OrderTypesUri, PoleInspection(policy: null), ClientOptions);
        var loaded = await client.GetFromJsonAsync<OrderType>(PoleInspectionUri, ClientOptions);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        loaded!.TargetPolicy.Should().BeNull();
        loaded.AttributesSchemaJson.Should().Be("""{"type":"object"}""");
    }

    [Fact]
    public async Task Put_ReplacesThenClearsTargetPolicy()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        await client.PostAsJsonAsync(OrderTypesUri, PoleInspection(policy: null), ClientOptions);

        var replaced = await client.PutAsJsonAsync(PoleInspectionUri, PoleInspection(FullPolicy()), ClientOptions);
        var afterReplace = await client.GetFromJsonAsync<OrderType>(PoleInspectionUri, ClientOptions);

        var cleared = await client.PutAsJsonAsync(PoleInspectionUri, PoleInspection(policy: null), ClientOptions);
        var afterClear = await client.GetFromJsonAsync<OrderType>(PoleInspectionUri, ClientOptions);

        replaced.StatusCode.Should().Be(HttpStatusCode.NoContent);
        afterReplace!.TargetPolicy.Should().BeEquivalentTo(FullPolicy());
        cleared.StatusCode.Should().Be(HttpStatusCode.NoContent);
        afterClear!.TargetPolicy.Should().BeNull();
    }

    [Fact]
    public async Task Post_InvalidTargetPolicy_Returns400AndStoresNothing()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.PostAsJsonAsync(
            OrderTypesUri, PoleInspection(FullPolicy() with { SelectionLimit = 0 }), ClientOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("orderTypeId").GetString().Should().Be("pole-inspection");
        body.RootElement.GetProperty("errors")[0].GetString().Should().Contain("selection limit");
        (await client.GetAsync(PoleInspectionUri)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_InvalidTargetPolicy_Returns400AndKeepsTheStoredType()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        await client.PostAsJsonAsync(OrderTypesUri, PoleInspection(FullPolicy()), ClientOptions);

        var selfFanOut = FullPolicy() with { FanOut = new OrderTargetFanOutPolicy("pole-inspection") };
        var response = await client.PutAsJsonAsync(PoleInspectionUri, PoleInspection(selfFanOut), ClientOptions);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var stored = await client.GetFromJsonAsync<OrderType>(PoleInspectionUri, ClientOptions);
        stored!.TargetPolicy.Should().BeEquivalentTo(FullPolicy());
    }
}
