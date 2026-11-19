using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GeoAssets.Workflow.Registry;

/// <summary>Registration of the order-type descriptor registry and the in-process verticals' descriptors (XD01-369).</summary>
public static class OrderTypeDescriptorServiceExtensions
{
    /// <summary>Registers <see cref="InMemoryOrderTypeDescriptorRegistry"/> as the host's <see cref="IOrderTypeDescriptorRegistry"/>.</summary>
    public static IServiceCollection AddOrderTypeDescriptorRegistry(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IOrderTypeDescriptorRegistry>(sp =>
            new InMemoryOrderTypeDescriptorRegistry(sp.GetServices<OrderTypeDescriptorSource>()));
        return services;
    }

    /// <summary>
    /// Contributes an in-process vertical's descriptors, published through the registry when it is created — the same
    /// upsert a remote vertical's <c>PUT</c> makes.
    /// </summary>
    public static IServiceCollection AddOrderTypeDescriptors(
        this IServiceCollection services, string verticalId, IReadOnlyList<OrderTypeDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(verticalId);
        ArgumentNullException.ThrowIfNull(descriptors);

        services.AddSingleton(new OrderTypeDescriptorSource(verticalId, descriptors));
        return services;
    }
}
