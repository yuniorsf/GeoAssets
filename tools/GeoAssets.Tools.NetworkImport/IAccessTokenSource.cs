namespace GeoAssets.Tools.NetworkImport;

/// <summary>The bearer token the tool presents to the kernel (asked for before every request).</summary>
internal interface IAccessTokenSource
{
    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
}
