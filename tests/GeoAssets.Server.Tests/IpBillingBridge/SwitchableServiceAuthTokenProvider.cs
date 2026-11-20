using GeoAssets.Identity.Authentication;

namespace GeoAssets.Server.Tests.IpBillingBridge;

/// <summary>The host's machine-to-machine token source, standing in for MSAL's client-credentials flow.</summary>
internal sealed class SwitchableServiceAuthTokenProvider : IServiceAuthTokenProvider
{
    public string Token { get; set; } = BridgeCallers.ServiceCallerToken;

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(Token);
}
