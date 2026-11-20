namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>A request the kernel stand-in received, with the bearer token it carried.</summary>
internal sealed record KernelRequest(HttpMethod Method, string Path, string? Authorization)
{
    public string? Token => Authorization is { } a && a.StartsWith("Bearer ", StringComparison.Ordinal) ? a["Bearer ".Length..] : null;
}
