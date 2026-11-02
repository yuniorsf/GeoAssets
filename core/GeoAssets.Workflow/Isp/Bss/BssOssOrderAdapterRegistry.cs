using System.Diagnostics.CodeAnalysis;

namespace GeoAssets.Workflow.Isp.Bss;

/// <summary>Resolves an <see cref="IBssOssOrderAdapter"/> by <see cref="IBssOssOrderAdapter.ProviderId"/>, case-insensitively (XD01-280's AC3).</summary>
public sealed class BssOssOrderAdapterRegistry
{
    private readonly Dictionary<string, IBssOssOrderAdapter> _byProviderId;

    public BssOssOrderAdapterRegistry(IEnumerable<IBssOssOrderAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);
        _byProviderId = adapters.ToDictionary(a => a.ProviderId, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyCollection<string> RegisteredProviderIds => _byProviderId.Keys;

    public bool TryResolve(string providerId, [NotNullWhen(true)] out IBssOssOrderAdapter? adapter)
    {
        if (string.IsNullOrEmpty(providerId))
        {
            adapter = null;
            return false;
        }

        return _byProviderId.TryGetValue(providerId, out adapter);
    }
}
