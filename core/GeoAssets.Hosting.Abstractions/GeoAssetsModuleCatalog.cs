namespace GeoAssets.Hosting.Abstractions;

/// <summary>The modules a host enabled, in the order <c>GeoAssets:Modules</c> lists them. Registered as a singleton.</summary>
public sealed class GeoAssetsModuleCatalog(IReadOnlyList<IGeoAssetsModule> modules)
{
    public IReadOnlyList<IGeoAssetsModule> Modules { get; } = modules ?? throw new ArgumentNullException(nameof(modules));
}
