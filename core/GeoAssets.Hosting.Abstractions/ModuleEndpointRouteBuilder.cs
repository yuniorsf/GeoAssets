using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace GeoAssets.Hosting.Abstractions;

/// <summary>
/// Hands one module its own data-source collection, so the endpoints it maps can be attributed to it (collision check)
/// before they are added to the host's route table. Everything else is the host's.
/// </summary>
internal sealed class ModuleEndpointRouteBuilder(IEndpointRouteBuilder host) : IEndpointRouteBuilder
{
    public ICollection<EndpointDataSource> DataSources { get; } = new List<EndpointDataSource>();

    public IServiceProvider ServiceProvider => host.ServiceProvider;

    public IApplicationBuilder CreateApplicationBuilder() => host.CreateApplicationBuilder();
}
