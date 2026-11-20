using GeoAssets.Identity.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace GeoAssets.Workflow.Rest;

/// <summary>
/// Registration of a vertical host's machine-to-machine clients of the kernel (XD01-382): the global target ledger
/// (<see cref="ITargetLedgerClient"/>), the bootstrap registration (<see cref="IVerticalRegistrationClient"/>) and the
/// generic organization-grant API (<see cref="IOrganizationGrantClient"/>, XD01-385), all against the kernel's root URL and
/// authenticating with the host's registered <see cref="IServiceAuthTokenProvider"/>.
/// </summary>
public static class KernelClientServiceExtensions
{
    /// <summary>The named <see cref="HttpClient"/> the clients use, with the kernel's root as its base address.</summary>
    public const string HttpClientName = "GeoAssetsKernel:service-caller";

    /// <summary>Registers the clients against <paramref name="kernelBaseUrl"/> (an absolute HTTP(S) URL).</summary>
    /// <exception cref="ArgumentException"><paramref name="kernelBaseUrl"/> is not an absolute HTTP(S) URL.</exception>
    public static IServiceCollection AddKernelServiceCallerClients(this IServiceCollection services, string kernelBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(kernelBaseUrl);
        if (!Uri.TryCreate(kernelBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var baseAddress)
            || (!string.Equals(baseAddress.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(baseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"'{kernelBaseUrl}' is not an absolute HTTP(S) URL.", nameof(kernelBaseUrl));
        }

        services.AddHttpClient(HttpClientName, client => client.BaseAddress = baseAddress);
        services.AddTransient<ITargetLedgerClient>(sp => new RestTargetLedgerClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetService<IServiceAuthTokenProvider>()));
        services.AddTransient<IVerticalRegistrationClient>(sp => new RestVerticalRegistrationClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetService<IServiceAuthTokenProvider>()));
        services.AddTransient<IOrganizationGrantClient>(sp => new RestOrganizationGrantClient(
            sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName),
            sp.GetService<IServiceAuthTokenProvider>()));

        return services;
    }
}
