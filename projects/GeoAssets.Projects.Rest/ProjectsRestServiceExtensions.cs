using GeoAssets.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Projects.Rest;

/// <summary>
/// DI registration for the REST-backed <see cref="IProjectClient"/> (XD01-142) — always
/// pointed at <c>GeoAssets.Server</c>, matching <c>WorkflowRestServiceExtensions.AddWorkflowRest</c>'s
/// shape.
/// </summary>
public static class ProjectsRestServiceExtensions
{
    /// <summary>
    /// Registers <see cref="RestProjectClient"/> pointed at <paramref name="baseUrl"/> (e.g.
    /// <c>http://localhost:5080/api/projects</c> — the prefix
    /// <c>ProjectsRestApiExtensions.MapProjectsApi</c> mounts on the server).
    /// </summary>
    public static IServiceCollection AddProjectsRest(this IServiceCollection services, string baseUrl)
    {
        services.AddHttpClient();

        services.AddSingleton<IProjectClient>(sp => new RestProjectClient(BuildClient(sp, baseUrl)));

        return services;
    }

    private static HttpClient BuildClient(IServiceProvider sp, string baseUrl)
    {
        // Named "GeoAssetsServer" client so the host's CIAM AuthorizationMessageHandler (Web's
        // Program.cs) also covers Projects calls — same reasoning as
        // WorkflowRestServiceExtensions.BuildClient/RestProviderFactory.BuildClient.
        var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient("GeoAssetsServer");
        client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        return client;
    }
}
