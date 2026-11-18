using GeoAssets.Provider.Rest;

namespace GeoAssets.Server.Tests.Architecture.Fixtures;

internal sealed class AsyncFixture
{
    public static async Task<int> ComputeAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(0, cancellationToken).ConfigureAwait(false);
        var options = new GeoCoreFederationOptions();
        return options.GetHashCode();
    }
}
