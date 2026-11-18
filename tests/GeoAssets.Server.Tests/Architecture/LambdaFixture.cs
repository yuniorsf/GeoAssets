using GeoAssets.Provider.Rest;

namespace GeoAssets.Server.Tests.Architecture.Fixtures;

internal sealed class LambdaFixture
{
    public static Func<int> Create() => () => new GeoCoreFederationOptions().GetHashCode();
}
