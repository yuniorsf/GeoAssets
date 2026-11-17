using GeoAssets.Core.Isp.NeutralOperator;
using GeoAssets.Core.Isp.NeutralOperator.Occupancy;
using GeoAssets.Workflow.Isp.NeutralOperator.SharedUse;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.TestKit;

namespace GeoAssets.Server.Tests;

/// <summary>
/// Seeding shared by the shared-use endpoint tests (XD01-316): a pole-work order of
/// organization A on poles <c>f-001…</c>, each with one activity, and the occupancy of those
/// poles — organization C on every pole, organization B on the second one.
/// </summary>
internal static class SharedUseApiSeed
{
    public const string Order = "ord-1";
    public const string Activity = "relocate-attachment";

    public static string Pole(int number) => $"f-{number:000}";

    public static string ChildOf(Guid occupant, string orderId = Order) => SharedUseDispatchPlanner.ChildOrderId(orderId, occupant);

    public static async Task<OrderTargetsApiHost> StartAsync(
        string status = ServiceOrderStatus.Draft,
        int poles = 2,
        bool occupied = true,
        bool mapOccupancyRoutes = false,
        Func<IServiceOrderRepository, IServiceOrderRepository>? decorateOrders = null)
    {
        var host = await OrderTargetsApiHost.StartAsync(
            mapOrderRoutes: true, mapOccupancyRoutes: mapOccupancyRoutes, mapSharedUseRoutes: true, decorateOrders: decorateOrders);
        await host.SeedOrderAsync(Order, SharedUseOrderTypes.PoleWorkId, status);
        await SeedTargetsAsync(host, Order, [Activity], [.. Enumerable.Range(1, poles).Select(Pole)]);
        if (occupied)
        {
            for (var pole = 1; pole <= poles; pole++)
            {
                await OccupyAsync(host, Pole(pole), pole == 2
                    ? [Entry(Pole(pole), 1, OrderTargetsApiHost.OrgC), Entry(Pole(pole), 2, OrderTargetsApiHost.OrgB)]
                    : [Entry(Pole(pole), 1, OrderTargetsApiHost.OrgC)]);
            }
        }

        return host;
    }

    public static Task<int> SeedTargetsAsync(OrderTargetsApiHost host, string orderId, string[] activities, params string[] poles) =>
        host.Store.AddRangeAsync(
            orderId,
            [.. poles.Select(pole => new ServiceOrderTargetBuilder()
                .WithId($"{orderId}-{pole}")
                .WithOrderId(orderId)
                .WithFeatureId(pole)
                .WithActivityIds(activities)
                .WithOrganizationId(OrderTargetsApiHost.OrgA)
                .Build())],
            CancellationToken.None);

    public static PoleOccupancyEntry Entry(
        string pole, int? slot, Guid? occupant, PoleAttachmentOccupantStatus status = PoleAttachmentOccupantStatus.AuthorizedContracted)
    {
        var builder = new PoleOccupancyEntryBuilder().ForPole(pole).OwnedBy(OrderTargetsApiHost.OrgA).WithStatus(status).OccupiedBy(occupant);
        return (slot is { } index ? builder.OnSlot(index) : builder.BeyondRegulatedSlots()).Build();
    }

    public static Task<PoleOccupancy> OccupyAsync(OrderTargetsApiHost host, string pole, params PoleOccupancyEntry[] entries) =>
        host.Occupancy.ForOrganization(OrderTargetsApiHost.OrgA).ReplaceEntriesAsync(pole, PoleOccupancy.UnsavedVersion, entries, CancellationToken.None);

    /// <summary>The body of a dispatch: the order's row version as the wire carries it.</summary>
    public static object DispatchBody(byte[]? rowVersion = null) => new { rowVersion = Convert.ToBase64String(rowVersion ?? []) };

    public static Task<HttpResponseMessage> DispatchAsync(HttpClient client, string orderId = Order) =>
        client.PostJsonAsync($"service-orders/{orderId}/shared-use/dispatch", DispatchBody());
}
