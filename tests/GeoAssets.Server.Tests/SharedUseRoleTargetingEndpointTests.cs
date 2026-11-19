using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Core.Models;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Occupancy;
using GeoAssets.Workflow.Selection.Strategies;
using GeoAssets.Workflow.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using static GeoAssets.Server.Tests.OrderTargetsApiAssertions;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Acceptance test of XD01-368 on the host the occupancy and shared-use endpoint tests use
/// (<see cref="OrderTargetsApiHost"/>: the real routes, selection service, strategies, rules and staging service over
/// in-memory stores): a new asset type tagged <c>occupiable-support</c> is selectable by the shared-use orders and its
/// features are poles, with no pole asset type configured anywhere.
/// </summary>
public sealed class SharedUseRoleTargetingEndpointTests
{
    private const string Apply = "service-orders/ord-1/targets/selection/apply";
    private const string SupportRole = "occupiable-support";

    /// <summary>A host with no configured pole asset type: only the role says what a pole is.</summary>
    private static async Task<OrderTargetsApiHost> HostAsync()
    {
        var host = await OrderTargetsApiHost.StartAsync(
            mapOccupancyRoutes: true,
            configureServices: services =>
            {
                services.RemoveAll<OrderOccupancyOptions>();
                services.AddSingleton(new OrderOccupancyOptions());
            });
        await host.SeedOrderAsync(orderTypeId: SharedUseOrderTypes.PoleWorkId);
        return host;
    }

    private static object IdentifierList(params string[] featureIds) => new
    {
        strategyId = "identifier-list",
        parameters = new { attributeKey = IdentifierListSelectionStrategy.FeatureIdKey, values = featureIds },
    };

    [Fact]
    public async Task NewAssetTypeTaggedOccupiableSupport_IsSelectableBySharedUseOrders_WithoutConfiguration()
    {
        await using var host = await HostAsync();
        var support = new AssetType { Id = Guid.NewGuid(), Name = "Brand-new support", Roles = [SupportRole] };
        var plain = new AssetType { Id = Guid.NewGuid(), Name = "Brand-new plain" };
        host.Assets.AddAssetType(support);
        host.Assets.AddAssetType(plain);
        host.Assets.Add(OrderTargetsApiHost.Asset("support-1", 1, 1, OrderTargetsApiHost.OrgA, support.Id.ToString()));
        host.Assets.Add(OrderTargetsApiHost.Asset("plain-1", 2, 2, OrderTargetsApiHost.OrgA, plain.Id.ToString()));
        using var client = host.ClientFor("admin");

        var response = await client.PostJsonAsync(Apply, IdentifierList("support-1", "plain-1"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var targets = host.Store.Snapshot();
        targets.Select(t => t.FeatureId).Should().Equal(["support-1"], "a feature of a type without the role is not selectable");
        targets.Single().AssetTypeId.Should().Be(support.Id.ToString());

        var targetId = targets.Single().Id;
        using var occupancy = await client.GetAsync(Path($"service-orders/ord-1/targets/{targetId}/occupancy"));
        var body = await occupancy.ReadJsonAsync();
        body.GetProperty("poleFeatureId").GetString().Should().Be("support-1");

        using var staged = await client.PutAsJsonAsync(
            Path($"service-orders/ord-1/targets/{targetId}/occupancy-changes"),
            new
            {
                changes = new[]
                {
                    new
                    {
                        kind = (int)OccupancyChangeKind.Add,
                        slotIndex = (int?)1,
                        entryId = Guid.NewGuid(),
                        status = (int)PoleAttachmentOccupantStatus.AuthorizedContracted,
                        occupantOrganizationId = OccupancyChangeBuilder.DefaultOccupantOrganizationId,
                        operatorCnpj = "98.765.432/0001-10",
                        cableDiameterMm = 9.5m,
                        cableLoadDaN = 80m,
                    },
                },
            });
        staged.StatusCode.Should().Be(HttpStatusCode.OK, "a feature of a type carrying the role is a pole");
    }

    [Fact]
    public async Task FeatureOfNewAssetTypeWithoutTheRole_IsNotSelectable_AndItsTargetIsNotAPole()
    {
        await using var host = await HostAsync();
        var plain = new AssetType { Id = Guid.NewGuid(), Name = "Brand-new plain" };
        host.Assets.AddAssetType(plain);
        host.Assets.Add(OrderTargetsApiHost.Asset("plain-1", 2, 2, OrderTargetsApiHost.OrgA, plain.Id.ToString()));
        using var client = host.ClientFor("admin");

        using var response = await client.PostJsonAsync(Apply, IdentifierList("plain-1"));

        host.Store.Snapshot().Should().BeEmpty();
        response.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);

        await host.Store.AddRangeAsync(
            "ord-1",
            [new ServiceOrderTargetBuilder().WithId("ord-1-plain").WithOrderId("ord-1").WithFeatureId("plain-1").WithAssetTypeId(plain.Id.ToString())
                .WithOrganizationId(OrderTargetsApiHost.OrgA).Build()],
            CancellationToken.None);
        using var occupancy = await client.GetAsync(Path("service-orders/ord-1/targets/ord-1-plain/occupancy"));
        await occupancy.ShouldBeProblemAsync(HttpStatusCode.UnprocessableEntity, OccupancyProblemCodes.NotAPole);
    }
}
