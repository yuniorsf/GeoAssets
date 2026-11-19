using System.Net;
using System.Text.Json;
using FluentAssertions;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Targets;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// The <see cref="OccupancyCapabilities.Commit"/> capability on the wire and on the approval route (XD01-361): a targeted
/// type that does not declare it approves through the default action append, the shared-use types still commit occupancy.
/// </summary>
public sealed class OccupancyCapabilityEndpointTests
{
    private static object Approve(string? resultingStatus = null) => new
    {
        action = (int)OrderActionType.Approve,
        performedBy = "whoever-the-body-says",
        performedAt = OrderTargetsApiHost.Now.UtcDateTime,
        resultingStatus,
    };

    [Fact]
    public async Task Approve_TargetedTypeWithoutCapability_GoesThroughTheDefaultAppend()
    {
        await using var host = await OrderTargetsApiHost.StartAsync(mapOrderRoutes: true);
        host.OrderTypes.Register(new OrderType
        {
            Id = "targeted-no-capability",
            DisplayName = "Targeted, no capability",
            TargetPolicy = new OrderTargetPolicy { TargetAssetTypeIds = ["pole"] },
        });
        await host.SeedOrderAsync("ord-targeted", "targeted-no-capability", ServiceOrderStatus.InProgress);
        using var client = host.ClientFor("supervisor-a");

        var response = await client.PostJsonAsync("service-orders/ord-targeted/actions", Approve());

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var order = (await host.Orders.GetByIdAsync("ord-targeted"))!;
        order.Status.Should().Be(ServiceOrderStatus.InProgress, "the occupancy approval would have refused an approval from a status without one");
        order.ActionLog.Should().ContainSingle().Which.PerformedBy
            .Should().Be("whoever-the-body-says", "the default append stores the entry as sent; the occupancy commit logs the caller");
        host.Outbox.Messages.Should().BeEmpty("the occupancy commit service never ran");
    }

    [Fact]
    public async Task Approve_SharedUseType_StillCommitsOccupancy()
    {
        await using var host = await SharedUseApiSeed.StartAsync(status: ServiceOrderStatus.Completed, poles: 1, occupied: false);
        using var client = host.ClientFor("supervisor-a");

        var response = await client.PostJsonAsync($"service-orders/{SharedUseApiSeed.Order}/actions", Approve());

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var order = (await host.Orders.GetByIdAsync(SharedUseApiSeed.Order))!;
        order.Status.Should().Be(SharedUseOrderTypes.ApprovedStateKey);
        order.ActionLog.Should().ContainSingle().Which.PerformedBy
            .Should().Be(OrderTargetsApiHost.Identities["supervisor-a"].UserId.ToString(), "the commit path logs the caller, never the body");
    }

    [Fact]
    public async Task OrderTypes_Get_IncludesCapabilities()
    {
        await using var host = await OrderTargetsApiHost.StartAsync(mapOrderRoutes: true);
        var repository = host.Server.Services.GetRequiredService<IOrderTypeRepository>();
        await repository.AddAsync(new OrderType { Id = "capable", DisplayName = "Capable", Capabilities = [OccupancyCapabilities.Commit] });
        using var client = host.ClientFor("supervisor-a");

        var response = await client.GetAsync(new Uri("order-types/capable", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("capabilities").EnumerateArray().Select(e => e.GetString())
            .Should().Equal(OccupancyCapabilities.Commit);
    }
}
