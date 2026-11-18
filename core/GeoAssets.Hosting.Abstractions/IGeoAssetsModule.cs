using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Hosting.Abstractions;

/// <summary>
/// A self-contained unit a GeoAssets host composes at startup (XD01-356, ADR-001 Part 1.1/1.2): the order-type chassis
/// contract every vertical is built on. A host lists its built-in modules in code and enables a subset, in order, through
/// <c>GeoAssets:Modules</c> (see <see cref="GeoAssetsModuleHostingExtensions.AddGeoAssetsModules"/>).
/// </summary>
public interface IGeoAssetsModule
{
    /// <summary>Stable, unique identifier the host's <c>GeoAssets:Modules</c> configuration refers to (e.g. <c>neutral-operator</c>).</summary>
    string Id { get; }

    /// <summary>Registers the module's services. Runs before the DI container is built.</summary>
    void ConfigureServices(IServiceCollection services, IConfiguration configuration);

    /// <summary>Maps the module's endpoints. Runs after the kernel's own routes are mapped.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
