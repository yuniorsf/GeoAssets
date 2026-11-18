using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Hosting.Abstractions;

/// <summary>
/// Optional asynchronous service-configuration step a <see cref="IGeoAssetsModule"/> can also implement (XD01-359) — for
/// registrations that need I/O before the container is built, such as warming a catalog from its database. Runs right after
/// the module's own <see cref="IGeoAssetsModule.ConfigureServices"/>, in the enabled order; only
/// <see cref="GeoAssetsModuleHostingExtensions.AddGeoAssetsModulesAsync"/> runs it.
/// </summary>
public interface IModuleServiceInitializer
{
    Task ConfigureServicesAsync(IServiceCollection services, IConfiguration configuration, CancellationToken cancellationToken);
}
