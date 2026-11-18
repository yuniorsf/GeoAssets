using GeoAssets.Provider.Rest;

namespace GeoAssets.Server.Tests.Architecture.Fixtures;

internal sealed class MethodBodyOnlyFixture
{
    public static int Compute()
    {
        var options = new GeoCoreFederationOptions();
        return options.GetHashCode();
    }
}
