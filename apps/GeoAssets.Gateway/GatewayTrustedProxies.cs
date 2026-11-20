using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace GeoAssets.Gateway;

/// <summary>
/// The client address behind a load balancer (XD01-393): <c>X-Forwarded-For</c> is believed only from the proxies
/// (<c>RateLimiting:TrustedProxies</c>, addresses) and networks (<c>RateLimiting:TrustedNetworks</c>, CIDR) configured —
/// none by default, and then <see cref="ConnectionInfo.RemoteIpAddress"/> is used as it is and the middleware isn't even
/// added. With some configured, ASP.NET's <see cref="ForwardedHeadersMiddleware"/> walks <c>X-Forwarded-For</c> from the
/// right while the hop that appended an entry is trusted, and makes the first untrusted entry the request's
/// <see cref="ConnectionInfo.RemoteIpAddress"/> — the client address <see cref="RateLimitPartitionKeyResolver"/> keys by.
/// <para>
/// Ordering with YARP's own forwarded headers: this runs first in the pipeline (before CORS and the rate limiter) and only
/// on <c>X-Forwarded-For</c> (never <c>-Proto</c>/<c>-Host</c>, so the scheme and host the gateway forwards are unchanged);
/// the routes' <c>X-Forwarded: Set</c> transform runs later, when <c>MapReverseProxy</c> forwards the request, and sets
/// <c>X-Forwarded-For</c> from that same, already-resolved <see cref="ConnectionInfo.RemoteIpAddress"/> — overwriting what
/// the caller or the load balancer sent, as before. A backend therefore sees the real client behind a trusted proxy, and
/// still never a caller-chosen address.
/// </para>
/// </summary>
internal static class GatewayTrustedProxies
{
    private const string ProxiesKey = "RateLimiting:TrustedProxies";
    private const string NetworksKey = "RateLimiting:TrustedNetworks";

    /// <summary>Reads the trusted proxies and networks and, when there is any, configures <see cref="ForwardedHeadersOptions"/> for them.</summary>
    public static IServiceCollection AddGatewayTrustedProxies(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var proxies = Values(configuration, ProxiesKey)
            .Select(value => IPAddress.TryParse(value, out var address)
                ? address
                : throw new InvalidOperationException($"Configuration '{ProxiesKey}' has '{value}', which is not an IP address."))
            .ToList();
        var networks = Values(configuration, NetworksKey)
            .Select(value => IPNetwork.TryParse(value, out var network)
                ? network
                : throw new InvalidOperationException($"Configuration '{NetworksKey}' has '{value}', which is not a CIDR network (e.g. 10.0.0.0/8)."))
            .ToList();

        var settings = new TrustedProxySettings(proxies, networks);
        services.AddSingleton(settings);
        if (settings.IsEmpty) return services;

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            // The defaults trust loopback; only what is configured is trusted here.
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            foreach (var proxy in proxies) options.KnownProxies.Add(proxy);
            foreach (var network in networks) options.KnownIPNetworks.Add(network);
            // Every hop unwound must be a trusted one, so the chain needs no length cap of its own.
            options.ForwardLimit = null;
        });

        return services;
    }

    /// <summary>Adds the forwarded-headers middleware when a trusted proxy or network is configured; a no-op otherwise.</summary>
    public static IApplicationBuilder UseGatewayTrustedProxies(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var settings = app.ApplicationServices.GetService<TrustedProxySettings>();
        return settings is null || settings.IsEmpty ? app : app.UseForwardedHeaders();
    }

    private static IEnumerable<string> Values(IConfiguration configuration, string key) =>
        configuration.GetSection(key).GetChildren()
            .Select(child => child.Value?.Trim())
            .Where(value => !string.IsNullOrEmpty(value))
            .Select(value => value!);
}
