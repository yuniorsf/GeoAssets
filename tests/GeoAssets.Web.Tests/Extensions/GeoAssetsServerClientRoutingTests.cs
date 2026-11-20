using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.Identity.Authentication;
using GeoAssets.Identity.Authorization.Models;
using GeoAssets.Identity.Authorization.Services;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Rest;
using GeoAssets.Shared.Services;
using GeoAssets.Web.Extensions;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest;
using GeoAssets.Workflow.Rest.Routing;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Web.Tests.Extensions;

/// <summary>
/// XD01-397: through the real DI the Web host registers, a request to the gateway carries the token of the route it falls
/// under — the NeutralOperator scope for its route, the kernel's for everything else — and, with no route scope configured,
/// the kernel's for every request, as before.
/// </summary>
public sealed class GeoAssetsServerClientRoutingTests
{
    private const string Gateway = "http://gw";
    private const string Kernel = "api://kernel/access_as_user";
    private const string NeutralOperator = "api://no/access_as_user";
    private const string KernelOrderType = "kernel-order-type";

    private sealed class FakeNavigation : NavigationManager
    {
        public FakeNavigation() => Initialize("http://gw/", "http://gw/");

        protected override void NavigateToCore(string uri, NavigationOptions options)
        {
        }
    }

    private sealed class FakeTokenProvider : IAccessTokenProvider
    {
        public ValueTask<AccessTokenResult> RequestAccessToken() => throw new NotSupportedException();

        public ValueTask<AccessTokenResult> RequestAccessToken(AccessTokenRequestOptions options)
        {
            var token = new AccessToken { Value = $"token-for:{string.Join(' ', options.Scopes ?? [])}", Expires = new DateTimeOffset(2099, 1, 1, 0, 0, 0, TimeSpan.Zero) };
            return ValueTask.FromResult(new AccessTokenResult(AccessTokenResultStatus.Success, token, null, null));
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

    private sealed class Recorder(string registry) : HttpMessageHandler
    {
        public List<(string Path, string? Authorization)> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString()));
            var body = request.RequestUri.AbsolutePath.StartsWith("/api/registry/", StringComparison.Ordinal) ? registry : "{}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }

    private static string Registry() => JsonSerializer.Serialize(new[]
    {
        new { orderTypeId = SharedUseOrderTypes.PoleWorkId, apiBaseRoute = "/api/v/neutral-operator/workflow/service-orders" },
    });

    private static ServiceProvider Build(Recorder recorder, IConfiguration configuration)
    {
        var services = new ServiceCollection();
        services.AddSingleton(configuration);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IAccessTokenProvider>(new FakeTokenProvider());
        services.AddSingleton<NavigationManager>(new FakeNavigation());
        services.AddTransient<AuthorizationMessageHandler>();
        services.AddSingleton<IGeoAuthorizationService>(new Authorization());
        services.AddSingleton<WorkflowPrincipalFactory>();
        services.AddGeoAssetsServerClient(configuration, Gateway + "/").ConfigurePrimaryHttpMessageHandler(() => recorder);
        services.AddDescriptorRoutedWorkflowRest($"{Gateway}/api/workflow");
        services.AddDescriptorRoutedNeutralOperatorRest();
        return services.BuildServiceProvider();
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value, StringComparer.Ordinal)).Build();

    private static IConfiguration Routed() => Configuration(
        ("GeoAssetsServer:ApiScope", Kernel),
        ("GeoAssetsServer:RouteScopes:0:PathPrefix", "/api/v/neutral-operator/"),
        ("GeoAssetsServer:RouteScopes:0:Scope", NeutralOperator));

    private static Task CreateAsync(ServiceProvider provider, string orderTypeId) =>
        provider.GetRequiredService<IServiceOrderRepository>().AddAsync(new ServiceOrder { Id = "o1", OrderTypeId = orderTypeId, Status = ServiceOrderStatus.Draft });

    [Fact]
    public async Task ARequestToTheNeutralOperatorRoute_CarriesTheNeutralOperatorToken_AndTheRegistryReadTheKernels()
    {
        var recorder = new Recorder(Registry());
        using var provider = Build(recorder, Routed());

        await CreateAsync(provider, SharedUseOrderTypes.PoleWorkId);

        recorder.Requests.Should().Equal(
            ("/api/registry/order-types", $"Bearer token-for:{Kernel}"),
            ("/api/v/neutral-operator/workflow/service-orders", $"Bearer token-for:{NeutralOperator}"));
    }

    [Fact]
    public async Task AKernelOrderRequest_CarriesTheKernelToken_EvenWithARouteScopeConfigured()
    {
        var recorder = new Recorder(Registry());
        using var provider = Build(recorder, Routed());

        await CreateAsync(provider, KernelOrderType);

        recorder.Requests.Should().Equal(
            ("/api/registry/order-types", $"Bearer token-for:{Kernel}"),
            ("/api/workflow/service-orders", $"Bearer token-for:{Kernel}"));
    }

    [Fact]
    public async Task TheNamedClient_CarriesATokenPerRoot()
    {
        var recorder = new Recorder("[]");
        using var provider = Build(recorder, Routed());
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(GeoAssetsServerClientExtensions.ClientName);

        using var kernel = await http.GetAsync(new Uri("http://gw/api/workflow/service-orders"));
        using var neutral = await http.GetAsync(new Uri("http://gw/api/v/neutral-operator/workflow/service-orders"));

        recorder.Requests.Should().Equal(
            ("/api/workflow/service-orders", $"Bearer token-for:{Kernel}"),
            ("/api/v/neutral-operator/workflow/service-orders", $"Bearer token-for:{NeutralOperator}"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithoutARouteScope_EveryRequest_NeutralOperatorsIncluded_CarriesTheKernelToken(bool blankEntry)
    {
        var configuration = blankEntry
            ? Configuration(
                ("GeoAssetsServer:ApiScope", Kernel),
                ("GeoAssetsServer:RouteScopes:0:PathPrefix", "/api/v/neutral-operator/"),
                ("GeoAssetsServer:RouteScopes:0:Scope", ""))
            : Configuration(("GeoAssetsServer:ApiScope", Kernel));
        var recorder = new Recorder("[]");
        using var provider = Build(recorder, configuration);
        var http = provider.GetRequiredService<IHttpClientFactory>().CreateClient(GeoAssetsServerClientExtensions.ClientName);

        using var kernel = await http.GetAsync(new Uri("http://gw/api/workflow/service-orders"));
        using var neutral = await http.GetAsync(new Uri("http://gw/api/v/neutral-operator/workflow/service-orders"));

        recorder.Requests.Should().Equal(
            ("/api/workflow/service-orders", $"Bearer token-for:{Kernel}"),
            ("/api/v/neutral-operator/workflow/service-orders", $"Bearer token-for:{Kernel}"));
    }

    [Fact]
    public void TheShippedWebSettings_ListTheNeutralOperatorRoute_WithABlankScope_SoNothingChanges()
    {
        var path = Path.Combine(RepositoryRoot(), "apps", "GeoAssets.Web", "wwwroot", "appsettings.json");
        using var stream = File.OpenRead(path);
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();

        var routes = ApiScopeRoutes.FromConfiguration(configuration.GetSection("GeoAssetsServer"), new Uri(Gateway + "/"));

        routes.HasRouteScopes.Should().BeFalse();
        routes.DefaultScope.Should().NotBeNullOrWhiteSpace();
        var entry = configuration.GetSection("GeoAssetsServer:RouteScopes").GetChildren().Should().ContainSingle().Subject;
        entry["PathPrefix"].Should().Be("/api/v/neutral-operator/");
        entry["Scope"].Should().BeNullOrWhiteSpace();
    }

    [Fact]
    public void TheWebProgram_UsesTheClientExtension()
    {
        File.ReadAllText(Path.Combine(RepositoryRoot(), "apps", "GeoAssets.Web", "Program.cs")).Should().Contain("AddGeoAssetsServerClient(");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "GeoAssets.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("GeoAssets.sln was not found above the test output.");
    }
}
