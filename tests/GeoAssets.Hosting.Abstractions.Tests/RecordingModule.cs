using GeoAssets.Hosting.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Hosting.Abstractions.Tests;

internal class RecordingModule(
    string id,
    List<string>? calls = null,
    Action<IServiceCollection>? configure = null,
    Action<IEndpointRouteBuilder>? map = null) : IGeoAssetsModule
{
    public string Id { get; } = id;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        calls?.Add($"configure:{Id}");
        configure?.Invoke(services);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        calls?.Add($"map:{Id}");
        map?.Invoke(endpoints);
    }
}
