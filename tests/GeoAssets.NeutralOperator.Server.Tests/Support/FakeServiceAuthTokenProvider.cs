using GeoAssets.Identity.Authentication;

namespace GeoAssets.NeutralOperator.Server.Tests.Support;

/// <summary>The host's machine-to-machine token source, standing in for MSAL's client-credentials flow.</summary>
internal sealed class FakeServiceAuthTokenProvider : IServiceAuthTokenProvider
{
    public const string Token = "service-caller-token";

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) => Task.FromResult(Token);
}
