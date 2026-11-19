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

        // XD01-378: GET all also returns the code-registered types, so the posted one is found among them.
        all.Should().ContainSingle(t => string.Equals(t.Id, "pole-inspection", StringComparison.Ordinal)).Which.TargetPolicy.Should().BeEquivalentTo(FullPolicy());
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

    // ── RecipientAccess (XD01-333) ────────────────────────────────────────────

    [Theory]
    [InlineData(OrderRecipientAccess.Inform)]
    [InlineData(OrderRecipientAccess.Execute)]
    [InlineData(OrderRecipientAccess.Edit)]
    public async Task PostAndPut_RoundTripRecipientAccess(OrderRecipientAccess access)
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var created = await client.PostAsJsonAsync(OrderTypesUri, new OrderType { Id = "pole-inspection", DisplayName = "Pole inspection" }, ClientOptions);
        var afterCreate = await client.GetFromJsonAsync<OrderType>(PoleInspectionUri, ClientOptions);
        var replaced = await client.PutAsJsonAsync(
            PoleInspectionUri, new OrderType { Id = "pole-inspection", DisplayName = "Pole inspection", RecipientAccess = access }, ClientOptions);
        var afterReplace = await client.GetFromJsonAsync<OrderType>(PoleInspectionUri, ClientOptions);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        afterCreate!.RecipientAccess.Should().Be(OrderRecipientAccess.Inform, "a type that says nothing only informs its recipients");
        replaced.StatusCode.Should().Be(HttpStatusCode.NoContent);
        afterReplace!.RecipientAccess.Should().Be(access);
    }

    [Theory]
    [InlineData("POST", 3)]
    [InlineData("POST", -1)]
    [InlineData("PUT", 9)]
    public async Task PostAndPut_ARecipientAccessOutsideTheEnumeration_Returns400_AndStoresNothing(string verb, int recipientAccess)
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        if (string.Equals(verb, "PUT", StringComparison.Ordinal))
            await client.PostAsJsonAsync(OrderTypesUri, new OrderType { Id = "pole-inspection", DisplayName = "Pole inspection" }, ClientOptions);
        using var body = new StringContent(
            $$"""{"id":"pole-inspection","displayName":"Pole inspection","recipientAccess":{{recipientAccess}}}""",
            System.Text.Encoding.UTF8, "application/json");

        var response = string.Equals(verb, "PUT", StringComparison.Ordinal)
            ? await client.PutAsync(PoleInspectionUri, body)
            : await client.PostAsync(OrderTypesUri, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errors")[0].GetString().Should().Contain("recipientAccess");
        var stored = await client.GetAsync(PoleInspectionUri);
        if (string.Equals(verb, "PUT", StringComparison.Ordinal))
            (await stored.Content.ReadFromJsonAsync<OrderType>(ClientOptions))!.RecipientAccess.Should().Be(OrderRecipientAccess.Inform);
        else
            stored.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── RecipientActions (XD01-336) ───────────────────────────────────────────

    [Fact]
    public async Task PostAndPut_RoundTripRecipientActions()
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var created = await client.PostAsJsonAsync(OrderTypesUri, new OrderType { Id = "pole-inspection", DisplayName = "Pole inspection" }, ClientOptions);
        var afterCreate = await client.GetFromJsonAsync<OrderType>(PoleInspectionUri, ClientOptions);
        var replaced = await client.PutAsJsonAsync(
            PoleInspectionUri,
            new OrderType { Id = "pole-inspection", DisplayName = "Pole inspection", RecipientActions = [OrderActionType.Accept, OrderActionType.Annotate] },
            ClientOptions);
        var afterReplace = await client.GetFromJsonAsync<OrderType>(PoleInspectionUri, ClientOptions);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        afterCreate!.RecipientActions.Should().BeEmpty("a type that says nothing grants its recipients no action");
        replaced.StatusCode.Should().Be(HttpStatusCode.NoContent);
        afterReplace!.RecipientActions.Should().Equal(OrderActionType.Accept, OrderActionType.Annotate);
    }

    [Fact]
    public async Task OrderTypes_PostAndGet_NewVerb_RoundTripsAsString()
    {
        var survey = new OrderAction("Survey");
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();

        var created = await client.PostAsJsonAsync(
            OrderTypesUri,
            new OrderType
            {
                Id = "pole-inspection",
                DisplayName = "Pole inspection",
                RecipientActions = [survey, OrderAction.Accept],
                ActionPermissions = [new(survey, PolicyKind.Permission, "survey:run"), new(OrderAction.Approve, PolicyKind.Role, "Supervisor")],
                States = [new("Pending", "Pending"), new("Surveyed", "Surveyed", IsSuccess: true)],
                Transitions = [new("Pending", "Surveyed", survey), new("Surveyed", "Surveyed", OrderAction.Complete)],
            },
            ClientOptions);
        var json = await client.GetStringAsync(PoleInspectionUri);
        var stored = await client.GetFromJsonAsync<OrderType>(PoleInspectionUri, ClientOptions);

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        root.GetProperty("recipientActions").EnumerateArray().Select(e => e.ToString()).Should().Equal("Survey", "4");
        root.GetProperty("recipientActions")[0].ValueKind.Should().Be(JsonValueKind.String);
        root.GetProperty("recipientActions")[1].ValueKind.Should().Be(JsonValueKind.Number, "a historical action is still the number it always was");
        var permissions = root.GetProperty("actionPermissions").EnumerateArray().Select(p => p.GetProperty("action")).ToList();
        permissions.Select(a => a.ValueKind).Should().Equal(JsonValueKind.String, JsonValueKind.Number);
        permissions.Select(a => a.ToString()).Should().Equal("Survey", "1");
        var triggers = root.GetProperty("transitions").EnumerateArray().Select(t => t.GetProperty("triggerAction")).ToList();
        triggers.Select(a => a.ToString()).Should().Equal("Survey", "7");
        stored!.RecipientActions.Should().Equal(survey, OrderAction.Accept);
        stored.ActionPermissions.Select(p => p.Action).Should().Equal(survey, OrderAction.Approve);
        stored.Transitions.Select(t => t.TriggerAction).Should().Equal(survey, OrderAction.Complete);
    }

    [Theory]
    [InlineData("POST", 99)]
    [InlineData("POST", -1)]
    [InlineData("PUT", 14)]
    public async Task PostAndPut_ARecipientActionOutsideTheEnumeration_Returns400_AndStoresNothing(string verb, int recipientAction)
    {
        using var server = await BuildServerAsync();
        using var client = server.CreateClient();
        if (string.Equals(verb, "PUT", StringComparison.Ordinal))
            await client.PostAsJsonAsync(OrderTypesUri, new OrderType { Id = "pole-inspection", DisplayName = "Pole inspection" }, ClientOptions);
        using var body = new StringContent(
            $$"""{"id":"pole-inspection","displayName":"Pole inspection","recipientActions":[{{(int)OrderActionType.Accept}},{{recipientAction}}]}""",
            System.Text.Encoding.UTF8, "application/json");

        var response = string.Equals(verb, "PUT", StringComparison.Ordinal)
            ? await client.PutAsync(PoleInspectionUri, body)
            : await client.PostAsync(OrderTypesUri, body);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errors")[0].GetString().Should().Be("recipientActions may only name order actions.");
        var stored = await client.GetAsync(PoleInspectionUri);
        if (string.Equals(verb, "PUT", StringComparison.Ordinal))
            (await stored.Content.ReadFromJsonAsync<OrderType>(ClientOptions))!.RecipientActions.Should().BeEmpty();
        else
            stored.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
