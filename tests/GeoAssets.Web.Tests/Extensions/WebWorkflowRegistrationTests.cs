using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.NeutralOperator.Application;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Rest;
using GeoAssets.NeutralOperator.Rest.Routing;
using GeoAssets.Shared.Services;
using GeoAssets.Web.Extensions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Rest.Routing;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Web.Tests.Extensions;

/// <summary>
/// XD01-384: the registrations <c>GeoAssets.Web</c>'s <c>Program</c> makes for its workflow REST clients — the descriptor-routed
/// ones, over the gateway's base URL — resolve every port to its routed implementation, and the tenant portal client reaches
/// the vertical's route through the registry's descriptors.
/// </summary>
public sealed class WebWorkflowRegistrationTests
{
    private const string Gateway = "http://gw";

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

    private sealed class Gate(string registry) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            var body = request.RequestUri.AbsolutePath.StartsWith("/api/registry/", StringComparison.Ordinal) ? registry : "[]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static ServiceProvider Build(Gate gate)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["GeoAssetsServer:BaseUrl"] = Gateway,
            ["ServiceOrders:ApiBaseUrl"] = $"{Gateway}/api/workflow",
        }).Build());
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IGeoAuthorizationService>(new Authorization());
        services.AddSingleton<WorkflowPrincipalFactory>();
        services.AddHttpClient("GeoAssetsServer").ConfigurePrimaryHttpMessageHandler(() => gate);
        services.AddDescriptorRoutedWorkflowRest($"{Gateway}/api/workflow");
        services.AddDescriptorRoutedNeutralOperatorRest();
        services.AddIspTenantPortalRest();
        return services.BuildServiceProvider();
    }

    [Fact]
    public void TheRoutedPorts_Resolve()
    {
        using var provider = Build(new Gate("[]"));

        provider.GetRequiredService<IServiceOrderRepository>().Should().BeOfType<DescriptorRoutedServiceOrderRepository>();
        provider.GetRequiredService<IOrderTypeRepository>().Should().BeOfType<DescriptorRoutedOrderTypeRepository>();
        provider.GetRequiredService<IServiceOrderTargetReader>().Should().BeOfType<DescriptorRoutedServiceOrderTargets>();
        provider.GetRequiredService<IOrderTargetSelectionService>().Should().BeOfType<DescriptorRoutedOrderTargetSelectionService>();
        provider.GetRequiredService<IOrderOccupancyStagingService>().Should().BeOfType<DescriptorRoutedOrderOccupancyStagingService>();
        provider.GetRequiredService<ISharedUseDispatchService>().Should().BeOfType<DescriptorRoutedSharedUseDispatchClient>();
        provider.GetRequiredService<ISharedUseDispatchPlanReader>().Should().BeOfType<DescriptorRoutedSharedUseDispatchClient>();
        provider.GetRequiredService<IOrderApiRoutes>().DefaultRoot.Should().Be(new Uri("http://gw/api/workflow/"));
    }

    [Fact]
    public void TheTenantPortalClient_Resolves_WithTheRoutes()
    {
        using var provider = Build(new Gate("[]"));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<ITenantPortalRestClient>().Should().NotBeNull();
    }

    [Fact]
    public async Task TheTenantPortalClient_ReachesTheVerticalsRoute_ThePublishedDescriptorNames()
    {
        var gate = new Gate(JsonSerializer.Serialize(new[]
        {
            new { orderTypeId = TenantAttachmentRequestOrderTypes.AttachmentRequestId, apiBaseRoute = "/api/v/neutral-operator/workflow/service-orders" },
        }));
        using var provider = Build(gate);
        using var scope = provider.CreateScope();

        await scope.ServiceProvider.GetRequiredService<ITenantPortalRestClient>().GetMyAttachmentRequestsAsync();

        gate.Paths.Should().Equal(
            "/api/registry/order-types",
            "/api/v/neutral-operator/workflow/service-orders/by-order-type/isp-tenant-attachment-request");
    }

    [Fact]
    public async Task ASharedUseOrderCreatedThroughTheRegisteredRepository_IsPostedToTheVerticalsRoute()
    {
        var gate = new Gate(JsonSerializer.Serialize(new[]
        {
            new { orderTypeId = SharedUseOrderTypes.PoleWorkId, apiBaseRoute = "/api/v/neutral-operator/workflow/service-orders" },
        }));
        using var provider = Build(gate);

        await provider.GetRequiredService<IServiceOrderRepository>().AddAsync(new ServiceOrder { Id = "o1", OrderTypeId = SharedUseOrderTypes.PoleWorkId, Status = ServiceOrderStatus.Draft });

        gate.Paths.Should().Equal("/api/registry/order-types", "/api/v/neutral-operator/workflow/service-orders");
    }

    [Fact]
    public void TheWebProgram_RegistersTheDescriptorRoutedClients_NotTheUnroutedOnes()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "apps", "GeoAssets.Web", "Program.cs"));

        program.Should().Contain("AddDescriptorRoutedWorkflowRest(serviceOrdersApiBaseUrl)");
        program.Should().Contain("AddDescriptorRoutedNeutralOperatorRest(");
        program.Should().NotContain("AddWorkflowRest(");
    }

    [Fact]
    public void TheMauiProgram_RegistersTheDescriptorRoutedClients_NotTheUnroutedOnes()
    {
        var program = File.ReadAllText(Path.Combine(RepositoryRoot(), "apps", "GeoAssets.MAUI", "MauiProgram.cs"));

        program.Should().Contain("AddDescriptorRoutedWorkflowRest(");
        program.Should().Contain("AddDescriptorRoutedNeutralOperatorRest(");
        program.Should().NotContain("AddWorkflowRest(");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("GeoAssets.sln was not found above the test output.");
    }
}
