using GeoAssets.Identity.Authorization.Modules;
using GeoAssets.NeutralOperator.Application.SharedUse;
using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.Workflow.Registry;
using GeoAssets.Hosting.Abstractions;
using GeoAssets.NeutralOperator.Application;
using GeoAssets.NeutralOperator.Persistence;
using GeoAssets.Workflow.Orders;
using GeoAssets.Workflow.Hosting;

namespace GeoAssets.NeutralOperator.Modules;

/// <summary>
/// The ISP neutral-operator vertical as an order-type chassis module (XD01-358, ADR-001 Phase 1; lifted out of the kernel
/// into this library by XD01-382, Phase 7): its order types, feature-action descriptors, permissions, occupancy staging and
/// commit, shared-use dispatch and clandestine-notice actions, and their routes. Both hosts compose this same unit:
/// <c>GeoAssets.NeutralOperator.Server</c> always, and <c>GeoAssets.Server</c> while its <c>GeoAssets:Modules</c> lists
/// <see cref="ModuleId"/> (the cut-over's rollback flag — the kernel wraps this module in its quarantine namespace with the
/// two things only the kernel host does: the in-process asset-mutation apply and the GIS export over its own catalog).
/// The module registers no connection string of its own: the host's <c>ServiceOrderDbContext</c> is the one it joins.
/// </summary>
public sealed class NeutralOperatorModule : IGeoAssetsModule
{
    public const string ModuleId = "neutral-operator";

    public string Id => ModuleId;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // The vertical's own context (XD01-380): pole occupancies and staged change sets in schema neutral_operator, over the
        // host's ServiceOrderDbContext's connection and joining its transaction — and the stores on it.
        services.AddNeutralOperatorPersistence();

        // Isp.NeutralOperator order types (XD01-263's clandestine notice, XD01-269's tenant attachment request) and the
        // shared pole use ones (XD01-316: the pole owner's order and the notice each occupant gets).
        services.ConfigureOrderTypeRegistry(RegisterOrderTypes);

        // Feature actions as data (XD01-369): on any feature whose asset type is an occupiable support, "Create shared-use
        // order" — published to the kernel's order-type registry exactly as a remote vertical would PUT it.
        services.AddOrderTypeDescriptors(ModuleId, [SharedUseDescriptor]);

        // The ISP tenant role bundle, seeded by the kernel's identity seeding (XD01-377).
        services.AddSingleton<IModulePermissionContributor, NeutralOperatorPermissionContributor>();

        // Staged occupancy changes on an order's targets (XD01-315); a target is a pole when its asset type carries the
        // occupiable-support role (XD01-368) — see OrderOccupancyHostExtensions.
        services.AddServerOrderOccupancyStaging(configuration);

        // Shared-use dispatch (XD01-316): a pole owner's order fans out into one order per occupant organization, each
        // with a notification staged on the host's workflow outbox.
        services.AddServerSharedUseDispatch();

        // Approving an order with targets commits the occupancy staged on its poles (XD01-318) — OccupancyApprovalHandler,
        // this module's order action handler on POST /service-orders/{id}/actions (XD01-360) — as a saga with the kernel
        // (XD01-383): pending until the kernel applies the asset mutation batch, compensated when it rejects it.
        services.AddServerOccupancyCommit(configuration);

        // IClandestineAttachmentService (XD01-263), backed by the host's IServiceOrderRepository — performed only by
        // ClandestineNoticeActionHandler, on POST /service-orders/{id}/actions after the rule chain allowed the action
        // (XD01-364).
        services.AddScoped<IClandestineAttachmentService, ClandestineAttachmentService>();
        services.AddScoped<IOrderActionHandler, ClandestineNoticeActionHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // Occupancy staging on a service order's targets (XD01-315) under /api/workflow.
        endpoints.MapServiceOrderOccupancyApi();

        // Shared-use dispatch (XD01-316) under /api/workflow.
        endpoints.MapSharedUseDispatchApi();

        // The tenant ISP self-service portal (XD01-269) has no routes of its own (XD01-365, ADR-001 smell H): it creates,
        // lists and disputes through the generic /api/workflow service-order routes, where ClandestineNoticeActionHandler
        // performs the dispute after the rule chain allowed it.
    }

    /// <summary>Registers this vertical's order types in <paramref name="registry"/>.</summary>
    public static void RegisterOrderTypes(OrderTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        ClandestineAttachmentOrderTypes.RegisterAll(registry);
        TenantAttachmentRequestOrderTypes.RegisterAll(registry);
        registry.RegisterSharedUseOrderTypes();
    }

    /// <summary>
    /// What an out-of-process host of this module publishes to the kernel's order-type registry (XD01-382): one descriptor
    /// per order type of this vertical, as <paramref name="registry"/> holds it, with its <see cref="OrderTypeDescriptor.TargetPolicy"/>
    /// and <see cref="OrderTypeDescriptor.TerminalStatuses"/> — what the global target ledger needs to reserve its orders'
    /// targets and to release them when they close — and the shared-use feature action of <see cref="SharedUseDescriptor"/>.
    /// </summary>
    public static IReadOnlyList<OrderTypeDescriptor> PublishedDescriptors(OrderTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        var own = new OrderTypeRegistry();
        RegisterOrderTypes(own);

        return [.. own.All
            .Select(t => registry.Find(t.Id) ?? t)
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .Select(Describe)];
    }

    private static OrderTypeDescriptor Describe(OrderType orderType)
    {
        var published = string.Equals(orderType.Id, SharedUseDescriptor.OrderTypeId, StringComparison.Ordinal)
            ? SharedUseDescriptor
            : new OrderTypeDescriptor
            {
                VerticalId = ModuleId,
                OrderTypeId = orderType.Id,
                DisplayNameKey = $"orderTypes.{orderType.Id}",
                ApiBaseRoute = SharedUseDescriptor.ApiBaseRoute,
            };

        return published with
        {
            TargetPolicy = orderType.TargetPolicy,
            TerminalStatuses = [.. orderType.States
                .Select(s => s.Key)
                .Where(status => ServiceOrderTransitions.IsTerminalState(orderType, status))],
        };
    }

    /// <summary>The shared-use pole-work order type as this vertical publishes it to the kernel's registry (XD01-369).</summary>
    public static OrderTypeDescriptor SharedUseDescriptor { get; } = new()
    {
        VerticalId = ModuleId,
        OrderTypeId = SharedUseOrderTypes.PoleWorkId,
        DisplayNameKey = "orderTypes.sharedUsePoleWork",
        ApiBaseRoute = "/api/workflow/service-orders",
        FeatureActions =
        [
            new FeatureActionDescriptor(
                OccupancyAssetTypeRoles.OccupiableSupport,
                "create",
                "featureActions.createSharedUseOrder",
                SharedUseOrderTypes.PoleWorkId,
                "/api/workflow/service-orders"),
        ],
    };
}
