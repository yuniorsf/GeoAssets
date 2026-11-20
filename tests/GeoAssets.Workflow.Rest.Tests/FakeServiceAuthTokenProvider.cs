using GeoAssets.Identity.Authentication;

namespace GeoAssets.Workflow.Rest.Tests;

/// <summary>A token source that hands out a different token on each call, as MSAL's does once a token expires.</summary>
internal sealed class FakeServiceAuthTokenProvider : IServiceAuthTokenProvider
{
    private int _issued;

    public int Issued => _issued;

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult($"service-token-{Interlocked.Increment(ref _issued)}");
}
