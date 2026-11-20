using System.Collections.Concurrent;

namespace GeoAssets.Workflow.Rest.Routing;

/// <summary>
/// One REST client per workflow API root (XD01-384), each created once — on first use — by <paramref name="create"/> over
/// the host's token-carrying client based at that root. The roots are the gateway's, so there are as many as the registry
/// names distinct vertical routes, plus the kernel's.
/// </summary>
public sealed class ClientsByRoot<TClient>(Func<Uri, TClient> create)
    where TClient : class
{
    private readonly ConcurrentDictionary<Uri, TClient> _clients = new();

    public TClient For(Uri root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return _clients.GetOrAdd(root, create);
    }
}
