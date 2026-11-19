using GeoAssets.NeutralOperator.Application.Occupancy;
using GeoAssets.NeutralOperator.UI.Components.ServiceOrders;
using GeoAssets.NeutralOperator.UI.Services.SharedUse;
using GeoAssets.Shared.Services.OrderDetail;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.NeutralOperator.UI;

/// <summary>Registers the NeutralOperator vertical's client UI (XD01-378) with a Web or MAUI host.</summary>
public static class NeutralOperatorUiServiceExtensions
{
    /// <summary>
    /// Contributes the vertical's components to the generic order detail: the occupancy panel for
    /// order types that commit occupancy, and the shared-use dispatch dialog for order types that
    /// fan out to their poles' occupants (XD01-316).
    /// </summary>
    public static IServiceCollection AddNeutralOperatorUi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOrderDetailPanel<OccupancyOrderPanel>(OccupancyCapabilities.Commit);
        services.AddOrderDispatchDialog<SharedUseDispatchDialog>(SharedUseDispatchDialogState.IsFanOut);
        return services;
    }
}
