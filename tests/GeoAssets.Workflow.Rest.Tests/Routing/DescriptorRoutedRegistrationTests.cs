using System.Net;
using System.Text;
using FluentAssertions;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Rest.Routing;
using GeoAssets.Workflow.Targets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests.Routing;

/// <summary>XD01-384: <see cref="WorkflowRestServiceExtensions.AddDescriptorRoutedWorkflowRest"/>.</summary>
public sealed class DescriptorRoutedRegistrationTests
{
    private static ServiceProvider Build(HttpMessageHandler handler, TimeProvider? clock = null, TimeSpan? refresh = null)
    {
        var services = new ServiceCollection();
        services.AddHttpClient("GeoAssetsServer").ConfigurePrimaryHttpMessageHandler(() => handler);
        if (clock is not null) services.AddSingleton(clock);
        services.AddDescriptorRoutedWorkflowRest("http://gw/api/workflow/", refresh);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void EveryPort_ResolvesToTheRoutedImplementation_Shared()
    {
        using var provider = Build(new GatewayDouble());

        provider.GetRequiredService<IServiceOrderRepository>().Should().BeOfType<DescriptorRoutedServiceOrderRepository>();
        provider.GetRequiredService<IServiceOrderReader>().Should().BeSameAs(provider.GetRequiredService<IServiceOrderRepository>());
        provider.GetRequiredService<IServiceOrderWriter>().Should().BeSameAs(provider.GetRequiredService<IServiceOrderRepository>());
        provider.GetRequiredService<IOrderTypeRepository>().Should().BeOfType<DescriptorRoutedOrderTypeRepository>();
        provider.GetRequiredService<IServiceOrderTargetReader>().Should().BeOfType<DescriptorRoutedServiceOrderTargets>();
        provider.GetRequiredService<IServiceOrderTargetWriter>().Should().BeSameAs(provider.GetRequiredService<IServiceOrderTargetReader>());
        provider.GetRequiredService<IOrderTargetSelectionService>().Should().BeOfType<DescriptorRoutedOrderTargetSelectionService>();
        provider.GetRequiredService<IOrderOccupancyStagingService>().Should().BeOfType<DescriptorRoutedOrderOccupancyStagingService>();
        provider.GetRequiredService<ISharedUseDispatchService>().Should().BeOfType<DescriptorRoutedSharedUseDispatchClient>();
        provider.GetRequiredService<ISharedUseDispatchPlanReader>().Should().BeSameAs(provider.GetRequiredService<ISharedUseDispatchService>());
        provider.GetRequiredService<IOrderApiRoutes>().Should().BeOfType<DescriptorOrderApiRoutes>();
        provider.GetRequiredService<RestMapSelectionClient>().Should().NotBeNull();
    }

    [Fact]
    public void TheDefaultRoot_IsTheBaseUrl_WithATrailingSlash()
    {
        using var provider = Build(new GatewayDouble());

        provider.GetRequiredService<IOrderApiRoutes>().DefaultRoot.Should().Be(new Uri("http://gw/api/workflow/"));
    }

    [Fact]
    public void TheRegistration_DoesNotRegisterTheUnroutedRestPorts()
    {
        using var provider = Build(new GatewayDouble());

        provider.GetService<RestServiceOrderRepository>().Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void ABlankBaseUrl_IsRejected(string? baseUrl)
    {
        var act = () => new ServiceCollection().AddDescriptorRoutedWorkflowRest(baseUrl!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ARelativeBaseUrl_IsRejected()
    {
        var act = () => new ServiceCollection().AddDescriptorRoutedWorkflowRest("api/workflow");

        act.Should().Throw<UriFormatException>();
    }

    [Fact]
    public async Task TheRegistryReadFromTheGateway_RoutesAnOrderTypeToItsVerticalRoot_WithTheRefreshIntervalGiven()
    {
        var published = "[]";
        var gateway = new RegistryGateway(() => published);
        var clock = new FakeTimeProvider();
        using var provider = Build(gateway, clock, TimeSpan.FromMinutes(1));
        var orders = provider.GetRequiredService<IServiceOrderRepository>();

        await orders.GetByOrderTypeAsync("shared-use-pole-work");
        published = """[ { "orderTypeId": "shared-use-pole-work", "apiBaseRoute": "/api/v/vertical-a/workflow/service-orders" } ]""";
        await orders.GetByOrderTypeAsync("shared-use-pole-work");
        clock.Advance(TimeSpan.FromMinutes(1));
        await orders.GetByOrderTypeAsync("shared-use-pole-work");

        gateway.Paths.Should().Equal(
            "/api/registry/order-types",
            "/api/workflow/service-orders/by-order-type/shared-use-pole-work",
            "/api/workflow/service-orders/by-order-type/shared-use-pole-work",
            "/api/registry/order-types",
            "/api/v/vertical-a/workflow/service-orders/by-order-type/shared-use-pole-work");
    }

    private sealed class RegistryGateway(Func<string> registry) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            var body = request.RequestUri.AbsolutePath.StartsWith("/api/registry/", StringComparison.Ordinal) ? registry() : "[]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
