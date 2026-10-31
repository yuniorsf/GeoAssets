using System.Net;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Repositories;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.Workflow;
using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Proves the real production <c>POST /api/workflow/order-types/validate-schema</c> endpoint
/// (XD01-205), exercised through the actual <c>MapServiceOrdersApi()</c> extension rather than
/// a hand-rolled copy — mirrors <see cref="ServiceOrderRulesEndpointTests"/>'s TestServer setup.
/// The DI wiring below registers every dependency <c>MapServiceOrdersApi()</c>'s *other*
/// routes need, even though none of them are exercised here: minimal-API route metadata is
/// built eagerly for the whole endpoint group on first request, so an unregistered parameter
/// type on any sibling route (e.g. <c>IOrderTypeRepository</c>) fails every route in the
/// group, not just its own.
/// </summary>
public class OrderTypeSchemaValidationEndpointTests
{
    private const string ValidSchema = """
    {
      "type": "object",
      "properties": { "severity": { "type": "integer", "minimum": 1, "maximum": 5 } },
      "required": ["severity"],
      "additionalProperties": false
    }
    """;

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

    private static StringContent SchemaBody(string schemaJson) =>
        new(schemaJson, Encoding.UTF8, MediaTypeNames.Application.Json);

    [Fact]
    public async Task ValidateSchema_ValidDraft202012Schema_Returns200WithValidTrue()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.PostAsync(
            new Uri("/api/workflow/order-types/validate-schema", UriKind.Relative), SchemaBody(ValidSchema));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("valid").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task ValidateSchema_MalformedJson_Returns400WithActionableErrors()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.PostAsync(
            new Uri("/api/workflow/order-types/validate-schema", UriKind.Relative), SchemaBody("{ not json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("valid").GetBoolean().Should().BeFalse();
        body.GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ValidateSchema_NonConformingSchema_Returns400WithActionableErrors()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        const string schema = """{ "type": "object", "required": "should-be-an-array" }""";

        var response = await client.PostAsync(
            new Uri("/api/workflow/order-types/validate-schema", UriKind.Relative), SchemaBody(schema));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        body.GetProperty("valid").GetBoolean().Should().BeFalse();
        body.GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ValidateSchema_EmptyBody_Returns400()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.PostAsync(
            new Uri("/api/workflow/order-types/validate-schema", UriKind.Relative), SchemaBody(string.Empty));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ValidateSchema_DoesNotPersistAnything_OnlyValidates()
    {
        // The endpoint's whole purpose is a dry-run check before persistence (XD01-205
        // summary: "catching malformed schemas before they corrupt live order intake") — it
        // must not write anywhere. Nothing to assert against a repository here since the
        // route takes no IOrderTypeRepository dependency at all; this test documents that
        // intent and would fail to compile/resolve if a future change wired one in without
        // also updating this suite's DI setup (BuildServerAsync intentionally registers none).
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var response = await client.PostAsync(
            new Uri("/api/workflow/order-types/validate-schema", UriKind.Relative), SchemaBody(ValidSchema));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
