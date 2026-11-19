using GeoAssets.Workflow.Orders;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Shared.Services.OrderDetail;

/// <summary>Registers the components verticals contribute to the order detail (XD01-378) — see <see cref="IOrderDetailPanelRegistry"/>.</summary>
public static class OrderDetailPanelServiceExtensions
{
    /// <summary>
    /// Registers <see cref="IOrderDetailPanelRegistry"/>, so a host that registers no vertical
    /// still resolves it, empty. Safe to call more than once.
    /// </summary>
    public static IServiceCollection AddOrderDetailPanels(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IOrderDetailPanelRegistry, OrderDetailPanelRegistry>();
        return services;
    }

    /// <summary>
    /// Shows <typeparamref name="TComponent"/> in the detail of every order whose type declares
    /// <paramref name="capability"/>. The component receives the panel parameters
    /// <see cref="IOrderDetailPanelRegistry"/> documents.
    /// </summary>
    public static IServiceCollection AddOrderDetailPanel<TComponent>(this IServiceCollection services, string capability)
        where TComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(capability);

        services.AddSingleton(new OrderDetailPanelRegistration(typeof(TComponent), capability));
        return services.AddOrderDetailPanels();
    }

    /// <summary>
    /// Dispatches the orders of the types <paramref name="appliesTo"/> accepts through
    /// <typeparamref name="TComponent"/> instead of the generic dispatch dialog. The component
    /// receives the dispatch-dialog parameters <see cref="IOrderDetailPanelRegistry"/> documents.
    /// </summary>
    public static IServiceCollection AddOrderDispatchDialog<TComponent>(this IServiceCollection services, Func<OrderType, bool> appliesTo)
        where TComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(appliesTo);

        services.AddSingleton(new OrderDispatchDialogRegistration(typeof(TComponent), appliesTo));
        return services.AddOrderDetailPanels();
    }
}
