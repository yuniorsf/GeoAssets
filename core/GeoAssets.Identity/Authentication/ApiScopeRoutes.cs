using Microsoft.Extensions.Configuration;

namespace GeoAssets.Identity.Authentication;

/// <summary>
/// Which access-token scope a client request through the gateway carries (XD01-397). Web and MAUI send one named
/// HttpClient (<c>"GeoAssetsServer"</c>) to every API on the gateway's origin; a request whose path falls under a
/// configured route prefix (<c>GeoAssetsServer:RouteScopes</c>) carries that route's scope, every other request the
/// default (<c>GeoAssetsServer:ApiScope</c>, the kernel's API). With no route scope configured every request maps to the
/// default, exactly as before. The route prefixes live in configuration only — this type names no route.
/// </summary>
public sealed class ApiScopeRoutes
{
    public const string ApiScopeKey = "ApiScope";
    public const string RouteScopesKey = "RouteScopes";

    private const string PathPrefixKey = "PathPrefix";
    private const string ScopeKey = "Scope";

    private readonly (ApiScopeRoute Route, string ResolvedPath)[] _resolvedByLength;

    public ApiScopeRoutes(Uri baseUrl, string? defaultScope, IEnumerable<ApiScopeRoute>? routes = null)
    {
        ArgumentNullException.ThrowIfNull(baseUrl);
        if (!baseUrl.IsAbsoluteUri)
            throw new ArgumentException("The base URL must be absolute.", nameof(baseUrl));

        BaseUrl = WithTrailingSlash(baseUrl);
        DefaultScope = string.IsNullOrWhiteSpace(defaultScope) ? null : defaultScope.Trim();

        var kept = new List<ApiScopeRoute>();
        var prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var route in routes ?? [])
        {
            if (route is null || string.IsNullOrWhiteSpace(route.PathPrefix) || string.IsNullOrWhiteSpace(route.Scope))
                continue;

            var prefix = NormalizePrefix(route.PathPrefix);
            if (!prefixes.Add(prefix))
                throw new ArgumentException($"The route prefix '{prefix}' is configured more than once.", nameof(routes));

            kept.Add(new ApiScopeRoute(prefix, route.Scope.Trim()));
        }

        Routes = kept.AsReadOnly();
        _resolvedByLength = kept
            .Select(route => (route, ResolvePath(BaseUrl, route.PathPrefix)))
            .OrderByDescending(entry => entry.Item2.Length)
            .ToArray();

        var all = new List<string>();
        if (DefaultScope is not null)
            all.Add(DefaultScope);
        var additional = new List<string>();
        foreach (var route in kept)
        {
            if (!all.Contains(route.Scope, StringComparer.Ordinal))
                all.Add(route.Scope);
            if (!string.Equals(route.Scope, DefaultScope, StringComparison.Ordinal)
                && !additional.Contains(route.Scope, StringComparer.Ordinal))
                additional.Add(route.Scope);
        }

        AllScopes = all.AsReadOnly();
        AdditionalScopes = additional.AsReadOnly();
    }

    /// <summary>The gateway's root, with a trailing <c>/</c>.</summary>
    public Uri BaseUrl { get; }

    /// <summary>The scope a request under no configured route carries; null when none is configured.</summary>
    public string? DefaultScope { get; }

    /// <summary>The kept, normalised route entries, in configuration order.</summary>
    public IReadOnlyList<ApiScopeRoute> Routes { get; }

    public bool HasRouteScopes => Routes.Count > 0;

    /// <summary>The default scope (if any) first, then each route scope not already listed.</summary>
    public IReadOnlyList<string> AllScopes { get; }

    /// <summary>The route scopes that differ from the default — what sign-in must ask consent for.</summary>
    public IReadOnlyList<string> AdditionalScopes { get; }

    /// <summary>
    /// The scope <paramref name="requestUri"/> carries: the longest configured route prefix (resolved relative to
    /// <see cref="BaseUrl"/>'s path, on its scheme, host and port) the path falls under, else <see cref="DefaultScope"/> —
    /// also for a URI outside <see cref="BaseUrl"/>, whose caller decides whether it gets a token at all.
    /// </summary>
    public string? ScopeFor(Uri requestUri)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        if (!requestUri.IsAbsoluteUri)
            throw new ArgumentException("The request URI must be absolute.", nameof(requestUri));

        if (_resolvedByLength.Length == 0 || !SameOrigin(requestUri, BaseUrl))
            return DefaultScope;

        var path = requestUri.AbsolutePath;
        foreach (var (route, resolvedPath) in _resolvedByLength)
        {
            if (path.StartsWith(resolvedPath, StringComparison.OrdinalIgnoreCase)
                || path.Equals(resolvedPath[..^1], StringComparison.OrdinalIgnoreCase))
                return route.Scope;
        }

        return DefaultScope;
    }

    /// <summary>
    /// Reads <c>ApiScope</c> and each child of <c>RouteScopes</c> (<c>PathPrefix</c>, <c>Scope</c>) from
    /// <paramref name="section"/> — e.g. <c>configuration.GetSection("GeoAssetsServer")</c>.
    /// </summary>
    public static ApiScopeRoutes FromConfiguration(IConfiguration section, Uri baseUrl)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(baseUrl);

        var routes = section.GetSection(RouteScopesKey).GetChildren()
            .Select(child => new ApiScopeRoute(child[PathPrefixKey] ?? "", child[ScopeKey] ?? ""))
            .ToList();
        return new ApiScopeRoutes(baseUrl, section[ApiScopeKey], routes);
    }

    private static Uri WithTrailingSlash(Uri baseUrl)
    {
        if (baseUrl.AbsolutePath.EndsWith('/'))
            return baseUrl;

        var builder = new UriBuilder(baseUrl);
        builder.Path += "/";
        return builder.Uri;
    }

    private static string NormalizePrefix(string pathPrefix)
    {
        var trimmed = pathPrefix.Trim().Trim('/');
        return trimmed.Length == 0 ? "/" : "/" + trimmed + "/";
    }

    private static string ResolvePath(Uri baseUrl, string normalizedPrefix) =>
        baseUrl.AbsolutePath + normalizedPrefix[1..];

    private static bool SameOrigin(Uri requestUri, Uri baseUrl) =>
        string.Equals(requestUri.Scheme, baseUrl.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(requestUri.Host, baseUrl.Host, StringComparison.OrdinalIgnoreCase)
        && requestUri.Port == baseUrl.Port;
}
