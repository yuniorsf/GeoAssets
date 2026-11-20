using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GeoAssets.Workflow.Rest.Routing;

/// <summary>
/// <see cref="IOrderApiRoutes"/> over the kernel's order-type registry (<c>GET /api/registry/order-types</c>, XD01-369): each
/// descriptor's <see cref="Registry.OrderTypeDescriptor.ApiBaseRoute"/> is an absolute path on the origin of
/// <paramref name="defaultRoot"/> — the gateway's, so no vertical host name ever reaches the client — and its parent is that
/// order type's workflow API root (<c>/api/v/neutral-operator/workflow/service-orders</c> →
/// <c>/api/v/neutral-operator/workflow/</c>). Nothing here names a vertical: the routes are whatever the registry holds.
/// <list type="bullet">
///   <item>The registry is read once and kept for <paramref name="refreshInterval"/>, then read again on the next call; a
///   failed refresh keeps the routes already known. The first read's failure propagates, except a 403: a caller who may not
///   read the registry has every order type on <paramref name="defaultRoot"/>.</item>
///   <item><paramref name="registryHttp"/> is the token-carrying client of the gateway; the registry is requested at
///   <c>/api/registry/order-types</c> on <paramref name="defaultRoot"/>'s origin.</item>
/// </list>
/// </summary>
public sealed class DescriptorOrderApiRoutes : IOrderApiRoutes
{
    /// <summary>How long a read of the registry is used before it is read again, unless the host says otherwise.</summary>
    public static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMinutes(5);

    private const string RegistryRoute = "/api/registry/order-types";
    private const string CollectionSegment = "service-orders";

    private static readonly JsonSerializerOptions _opts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _registryHttp;
    private readonly TimeProvider _clock;
    private readonly TimeSpan _refreshInterval;

    private Snapshot? _current;
    private Task<Snapshot>? _pending;

    public DescriptorOrderApiRoutes(HttpClient registryHttp, Uri defaultRoot, TimeProvider clock, TimeSpan refreshInterval)
    {
        ArgumentNullException.ThrowIfNull(registryHttp);
        ArgumentNullException.ThrowIfNull(defaultRoot);
        ArgumentNullException.ThrowIfNull(clock);
        if (!defaultRoot.IsAbsoluteUri) throw new ArgumentException("The default root must be an absolute URI.", nameof(defaultRoot));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(refreshInterval, TimeSpan.Zero);

        _registryHttp = registryHttp;
        DefaultRoot = AsRoot(defaultRoot);
        _clock = clock;
        _refreshInterval = refreshInterval;
    }

    public Uri DefaultRoot { get; }

    public async Task<IReadOnlyList<Uri>> GetRootsAsync(CancellationToken ct) =>
        (await GetSnapshotAsync(ct).ConfigureAwait(false)).Roots;

    public async Task<Uri> ResolveAsync(string orderTypeId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderTypeId);

        var snapshot = await GetSnapshotAsync(ct).ConfigureAwait(false);
        return snapshot.RootsByOrderType.TryGetValue(orderTypeId, out var root) ? root : DefaultRoot;
    }

    /// <summary>
    /// The workflow API root of a descriptor's collection route, on <see cref="DefaultRoot"/>'s origin: the route's parent
    /// when it ends in <c>service-orders</c> (the documented shape), the route itself otherwise.
    /// </summary>
    internal Uri RootOf(string apiBaseRoute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiBaseRoute);

        var collection = AsRoot(new Uri(DefaultRoot, apiBaseRoute));
        var lastSegment = collection.Segments[^1].TrimEnd('/');
        return string.Equals(lastSegment, CollectionSegment, StringComparison.Ordinal) ? new Uri(collection, "..") : collection;
    }

    private async Task<Snapshot> GetSnapshotAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var current = Volatile.Read(ref _current);
        if (current is not null && _clock.GetUtcNow() - current.ReadAt < _refreshInterval) return current;

        var pending = Volatile.Read(ref _pending);
        if (pending is null)
        {
            var started = LoadAsync();
            pending = Interlocked.CompareExchange(ref _pending, started, null) ?? started;
        }

        try
        {
            var loaded = await pending.WaitAsync(ct).ConfigureAwait(false);
            Volatile.Write(ref _current, loaded);
            return loaded;
        }
        catch (Exception) when (current is not null && !ct.IsCancellationRequested)
        {
            // A failed refresh keeps the routes already known; the next call tries again.
            return current;
        }
        finally
        {
            if (pending.IsCompleted) _ = Interlocked.CompareExchange(ref _pending, null, pending);
        }
    }

    private async Task<Snapshot> LoadAsync()
    {
        // Shared by every caller waiting on it, so no caller's token cancels it; each caller stops waiting on its own token.
        using var response = await _registryHttp
            .GetAsync(new Uri(DefaultRoot, RegistryRoute), CancellationToken.None)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.Forbidden)
            return Build([]);

        response.EnsureSuccessStatusCode();
        var descriptors = await response.Content
            .ReadFromJsonAsync<DescriptorRoute[]>(_opts, CancellationToken.None)
            .ConfigureAwait(false) ?? [];

        return Build(descriptors);
    }

    private Snapshot Build(IReadOnlyCollection<DescriptorRoute> descriptors)
    {
        var rootsByOrderType = new Dictionary<string, Uri>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            if (string.IsNullOrWhiteSpace(descriptor.OrderTypeId) || string.IsNullOrWhiteSpace(descriptor.ApiBaseRoute)) continue;
            rootsByOrderType[descriptor.OrderTypeId] = RootOf(descriptor.ApiBaseRoute);
        }

        var roots = new List<Uri> { DefaultRoot };
        roots.AddRange(rootsByOrderType.Values
            .Where(root => root != DefaultRoot)
            .Distinct()
            .OrderBy(root => root.AbsoluteUri, StringComparer.Ordinal));

        return new Snapshot(rootsByOrderType, roots, _clock.GetUtcNow());
    }

    private static Uri AsRoot(Uri uri) =>
        uri.AbsoluteUri.EndsWith('/') ? uri : new Uri(uri.AbsoluteUri + "/", UriKind.Absolute);

    private sealed record Snapshot(IReadOnlyDictionary<string, Uri> RootsByOrderType, IReadOnlyList<Uri> Roots, DateTimeOffset ReadAt);

    /// <summary>The two members of a descriptor the routing needs, as the registry serializes them.</summary>
    private sealed record DescriptorRoute(string? OrderTypeId, string? ApiBaseRoute);
}
