using GeoAssets.NeutralOperator.Rest;
using GeoAssets.Workflow.Rest.Routing;

namespace GeoAssets.Workflow.Rest.Tests.Routing;

/// <summary>Every client the routed ports need, over one <see cref="GatewayDouble"/>.</summary>
internal sealed class RoutedClients(IOrderApiRoutes routes, GatewayDouble gateway)
{
    private HttpClient Http(Uri root) => new(gateway, disposeHandler: false) { BaseAddress = root };

    public ClientsByRoot<RestServiceOrderRepository> Repositories => field ??= new(root => new RestServiceOrderRepository(Http(root)));

    public ClientsByRoot<RestOrderTypeRepository> OrderTypes => field ??= new(root => new RestOrderTypeRepository(Http(root)));

    public ClientsByRoot<RestServiceOrderTargetRepository> Targets => field ??= new(root => new RestServiceOrderTargetRepository(Http(root)));

    public ClientsByRoot<RestOrderTargetSelectionService> Selections => field ??= new(root => new RestOrderTargetSelectionService(Http(root)));

    public ClientsByRoot<RestOrderOccupancyStagingService> Occupancy => field ??= new(root => new RestOrderOccupancyStagingService(Http(root)));

    public ClientsByRoot<RestSharedUseDispatchClient> Dispatch => field ??= new(root => new RestSharedUseDispatchClient(Http(root)));

    public ServiceOrderRouteLocator Locator => field ??= new(routes, Repositories);

    public DescriptorRoutedServiceOrderRepository Orders => field ??= new(routes, Repositories, Locator);
}
