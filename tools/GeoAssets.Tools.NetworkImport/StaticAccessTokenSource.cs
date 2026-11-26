namespace GeoAssets.Tools.NetworkImport;

/// <summary>A token given as is (<c>GEOASSETS_NETWORK_IMPORT_TOKEN</c>).</summary>
internal sealed class StaticAccessTokenSource(string token) : IAccessTokenSource
{
    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken) => Task.FromResult(token);
}
