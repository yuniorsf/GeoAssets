using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Core.Services;
using GeoAssets.Workflow.Orders;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>
/// Proves <see cref="WorkflowRestServiceExtensions.AddWorkflowRest"/> resolves its HttpClient
/// from the named "GeoAssetsServer" client (XD01-127) rather than an anonymous one — the bug
/// this fixed: GeoAssets.Web only attaches its CIAM AuthorizationMessageHandler to that named
/// client (Program.cs), so requests from an anonymous client silently skipped auth and every
/// call 401'd against GeoAssets.Server's FallbackPolicy.RequireAuthenticatedUser(). Any handler
/// chain a host attaches to "GeoAssetsServer" — the real auth handler in production, this fake
/// one here — must actually run for ServiceOrders/OrderType requests, not be bypassed.
/// </summary>
public class WorkflowRestServiceExtensionsTests
{
    [Fact]
    public async Task AddWorkflowRest_ServiceOrderRepository_RoutesThroughNamedGeoAssetsServerClient()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Array.Empty<ServiceOrder>(), options: GeoJsonSerializer.GetOptions())
            });

        var services = new ServiceCollection();
        services.AddHttpClient("GeoAssetsServer").ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddWorkflowRest("http://test/api/workflow");

        var repo = services.BuildServiceProvider().GetRequiredService<IServiceOrderRepository>();
        await repo.GetAllAsync();

        // Would be empty if BuildClient still requested an anonymous client — the named
        // client's pipeline (this fake handler; a real auth handler in production) would
        // never see the request.
        handler.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AddWorkflowRest_OrderTypeRepository_RoutesThroughNamedGeoAssetsServerClient()
    {
        var handler = new FakeHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(Array.Empty<OrderType>(), options: GeoJsonSerializer.GetOptions())
            });

        var services = new ServiceCollection();
        services.AddHttpClient("GeoAssetsServer").ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddWorkflowRest("http://test/api/workflow");

        var repo = services.BuildServiceProvider().GetRequiredService<IOrderTypeRepository>();
        await repo.GetAllAsync();

        handler.Requests.Should().ContainSingle();
    }
}
