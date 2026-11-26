namespace GeoAssets.Shared.Interfaces;

/// <summary>
/// How the map loads one WMS layer's tiles (XD01-96), answered by <see cref="IMapTileAccessProvider"/> and read by
/// <c>geoassets.js</c>. A layer on a server other than this app's own is loaded as before, by plain image requests
/// without a token (<see cref="Anonymous"/>). One on this app's own server needs a bearer token: the answer carries it
/// and when it expires (<see cref="Bearer"/>), or reports that none is available (<see cref="Unavailable"/>), in which
/// case the layer shows nothing instead of requesting tiles it would be refused.
/// </summary>
/// <param name="RequiresToken">Whether the layer's tiles are requested with a bearer token.</param>
/// <param name="AccessToken">The token, when one is required and available.</param>
/// <param name="ExpiresOn">When <paramref name="AccessToken"/> expires.</param>
public sealed record MapTileAccess(bool RequiresToken, string? AccessToken, DateTimeOffset? ExpiresOn)
{
    /// <summary>The layer is on another server: its tiles are requested without a token.</summary>
    public static MapTileAccess Anonymous { get; } = new(false, null, null);

    /// <summary>The layer needs a token and none can be acquired without user interaction.</summary>
    public static MapTileAccess Unavailable { get; } = new(true, null, null);

    /// <summary>The layer's tiles are requested with <paramref name="accessToken"/>, valid until <paramref name="expiresOn"/>.</summary>
    public static MapTileAccess Bearer(string accessToken, DateTimeOffset expiresOn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        return new MapTileAccess(true, accessToken, expiresOn);
    }

    /// <summary>Never prints the token, so a logged answer can't leak it.</summary>
    public override string ToString() =>
        $"{nameof(MapTileAccess)} {{ {nameof(RequiresToken)} = {RequiresToken}, " +
        $"{nameof(AccessToken)} = {(AccessToken is null ? "null" : "***")}, {nameof(ExpiresOn)} = {ExpiresOn?.ToString("O", System.Globalization.CultureInfo.InvariantCulture) ?? "null"} }}";
}
