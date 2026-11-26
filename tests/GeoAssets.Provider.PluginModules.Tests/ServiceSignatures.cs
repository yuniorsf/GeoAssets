using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Provider.PluginModules.Tests;

/// <summary>A comparable one-line signature per service registration: service, key, lifetime and implementation.</summary>
internal static class ServiceSignatures
{
    public static List<string> Of(IEnumerable<ServiceDescriptor> services) =>
        services.Select(Of).ToList();

    public static string Of(ServiceDescriptor d)
    {
        var implementationType = d.IsKeyedService ? d.KeyedImplementationType : d.ImplementationType;
        var instance = d.IsKeyedService ? d.KeyedImplementationInstance : d.ImplementationInstance;
        var factory = d.IsKeyedService ? (Delegate?)d.KeyedImplementationFactory : d.ImplementationFactory;
        return string.Join(
            " | ",
            d.ServiceType.FullName,
            d.ServiceKey?.ToString() ?? "-",
            d.Lifetime.ToString(),
            implementationType?.FullName ?? "-",
            instance?.GetType().FullName ?? "-",
            factory is null ? "-" : $"{factory.Method.DeclaringType?.FullName}.{factory.Method.Name}");
    }
}
