using GeoAssets.Identity.Authentication;

namespace GeoAssets.Provider.Rest.Tests;

/// <summary>Returns tokens from <paramref name="tokens"/> in order, cycling on exhaustion, and counts calls.</summary>
internal sealed class FakeServiceAuthTokenProvider(params string[] tokens) : IServiceAuthTokenProvider
{
    private int _index = -1;

    public int CallCount { get; private set; }

    public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        CallCount++;
        _index = (_index + 1) % tokens.Length;
        return Task.FromResult(tokens[_index]);
    }
}
