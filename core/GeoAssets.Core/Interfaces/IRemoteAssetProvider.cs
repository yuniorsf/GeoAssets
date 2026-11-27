namespace GeoAssets.Core.Interfaces;

/// <summary>
/// Implemented by an <see cref="IAssetProvider"/> that reads a GeoAssets server over HTTP (ADR-002 D6, XD01-436). The
/// map compares <see cref="BaseAddress"/>'s origin with its tile style's to tell whether that server's <c>/tiles</c>
/// carry this provider's features — read through <see cref="Providers.AssetProviderExtensions.UnwrapToConcrete"/>, as
/// <see cref="IProviderCapabilities"/> is.
/// </summary>
public interface IRemoteAssetProvider
{
    /// <summary>The server API's base address (e.g. <c>https://gateway/api/geoassets/</c>); null when unknown.</summary>
    Uri? BaseAddress { get; }
}
