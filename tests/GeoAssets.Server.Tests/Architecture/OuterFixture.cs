using GeoAssets.Provider.Rest;

namespace GeoAssets.Server.Tests.Architecture.Fixtures;

internal sealed class OuterFixture
{
    public static int Value => Inner.Compute();

    internal sealed class Inner
    {
        public static int Compute() => new GeoCoreFederationOptions().GetHashCode();
    }
}
