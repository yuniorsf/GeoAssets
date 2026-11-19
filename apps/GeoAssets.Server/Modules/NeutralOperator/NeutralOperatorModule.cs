using GeoAssets.Hosting.Abstractions;
using GeoAssets.Workflow.Isp.NeutralOperator;
using GeoAssets.Workflow.Orders;

namespace GeoAssets.Server.Modules.NeutralOperator;

/// <summary>
/// The ISP neutral-operator vertical as a module of this host (XD01-358, ADR-001 Phase 1): exactly the registrations and
/// endpoints <c>Program.cs</c> used to make by hand — moved, not rewritten — so that Phase 7 can lift the vertical into
/// <c>NeutralOperator.Server</c> as one unit. Enabled by listing <see cref="ModuleId"/> in <c>GeoAssets:Modules</c>.
/// </summary>
public sealed class NeutralOperatorModule : IGeoAssetsModule
{
    public const string ModuleId = "neutral-operator";

    public string Id => ModuleId;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString("GeoAssets")
            ?? throw new InvalidOperationException(
                $"The '{ModuleId}' module needs the 'GeoAssets' connection string.");

        // Isp.NeutralOperator order types (XD01-263's clandestine notice, XD01-269's tenant attachment request) and the
        // shared pole use ones (XD01-316: the pole owner's order and the notice each occupant gets).
        services.ConfigureOrderTypeRegistry(registry =>
        {
            ClandestineAttachmentOrderTypes.RegisterAll(registry);
            TenantAttachmentRequestOrderTypes.RegisterAll(registry);
            registry.RegisterSharedUseOrderTypes(configuration);
        });

        // Staged occupancy changes on an order's targets (XD01-315); which asset types are poles is this host's to
        // say — see OrderOccupancyHostExtensions.
        services.AddServerOrderOccupancyStaging(configuration);

        // Shared-use dispatch (XD01-316): a pole owner's order fans out into one order per occupant organization, each
        // with a notification staged on the host's workflow outbox.
        services.AddServerSharedUseDispatch();

        // Approving an order with targets commits the occupancy staged on its poles (XD01-318) — OccupancyApprovalHandler,
        // this module's order action handler on POST /service-orders/{id}/actions (XD01-360).
        services.AddServerOccupancyCommit();

        // With no broker configured, the asset mutation batches that approval stages are applied to this host's own
        // catalog by an in-process outbox relay (XD01-337); every other message stays pending.
        services.AddServerInProcessAssetMutationApply(configuration, connectionString);

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
}
