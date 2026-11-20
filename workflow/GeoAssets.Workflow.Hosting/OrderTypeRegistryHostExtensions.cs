using GeoAssets.Workflow.Orders;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// Lets a module add its order types to the host's <see cref="OrderTypeRegistry"/> (XD01-358) — the module's contribution
/// to what <c>AddOrderTypeRegistry</c>'s callback used to register by hand in <c>Program.cs</c>.
/// </summary>
public static class OrderTypeRegistryHostExtensions
{
    /// <summary>
    /// Applies <paramref name="configure"/> to the <see cref="OrderTypeRegistry"/> instance <c>AddOrderTypeRegistry</c>
    /// already registered, in call order after the registry's own seeded types.
    /// </summary>
    /// <exception cref="InvalidOperationException"><c>AddOrderTypeRegistry</c> has not been called on <paramref name="services"/>.</exception>
    public static IServiceCollection ConfigureOrderTypeRegistry(this IServiceCollection services, Action<OrderTypeRegistry> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var registry = services
            .LastOrDefault(d => d.ServiceType == typeof(OrderTypeRegistry))?
            .ImplementationInstance as OrderTypeRegistry
            ?? throw new InvalidOperationException(
                "No OrderTypeRegistry instance is registered: call AddOrderTypeRegistry before configuring it.");

        configure(registry);
        return services;
    }
}
