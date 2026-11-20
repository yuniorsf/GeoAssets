using GeoAssets.Identity.Authorization.Services;

namespace GeoAssets.Workflow.Hosting;

/// <summary>
/// The caller's identity as the kernel knows it (XD01-382, ADR-001 D3): this host has no identity database, so
/// <see cref="ServerWorkflowPrincipalFactory"/> — and through it every <c>ServiceOrderRules</c> decision, the one
/// authorization point for a service order — is built over <see cref="KernelGeoAuthorizationService"/> and
/// <see cref="KernelOwnOrganizationGrantSource"/>, both calling the kernel's caller-scoped identity routes with the
/// caller's own token. The <c>RequireAuthorization("serviceorders:read")</c> permission policies run on the same service.
/// </summary>
public static class KernelIdentityServiceExtensions
{
    /// <summary>The named <see cref="HttpClient"/> of the kernel's identity routes.</summary>
    public const string HttpClientName = "GeoAssetsKernel:identity";

    public static IServiceCollection AddKernelIdentity(this IServiceCollection services, Uri kernelBaseAddress)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(kernelBaseAddress);

        services.AddHttpContextAccessor();
        services.AddTransient<ForwardedBearerTokenHandler>();
        services.AddHttpClient(HttpClientName, client => client.BaseAddress = new Uri(kernelBaseAddress, "api/identity/"))
            .AddHttpMessageHandler<ForwardedBearerTokenHandler>();

        services.AddScoped<IGeoAuthorizationService>(sp =>
            new KernelGeoAuthorizationService(sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));
        services.AddScoped<IOwnOrganizationGrantSource>(sp =>
            new KernelOwnOrganizationGrantSource(sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName)));
        services.AddScoped(sp => new ServerWorkflowPrincipalFactory(
            sp.GetRequiredService<IGeoAuthorizationService>(), sp.GetRequiredService<IOwnOrganizationGrantSource>()));

        return services;
    }
}
