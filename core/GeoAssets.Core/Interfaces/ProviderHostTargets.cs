namespace GeoAssets.Core.Interfaces;

/// <summary>
/// The host kinds an <see cref="IProviderPluginModule"/> can run in (ADR-002 D1/D7). A host
/// skips a module whose <see cref="IProviderPluginModule.SupportedHosts"/> doesn't include it.
/// </summary>
[Flags]
public enum ProviderHostTargets
{
    /// <summary>No host — a module declaring this is never registered.</summary>
    None = 0,

    /// <summary>ASP.NET Core server hosts (the kernel, the vertical microservices).</summary>
    Server = 1,

    /// <summary>Blazor WebAssembly (<c>GeoAssets.Web</c>).</summary>
    Browser = 2,

    /// <summary>MAUI mobile/desktop (<c>GeoAssets.MAUI</c>).</summary>
    MobileDesktop = 4,

    /// <summary>Every host.</summary>
    All = Server | Browser | MobileDesktop,
}
